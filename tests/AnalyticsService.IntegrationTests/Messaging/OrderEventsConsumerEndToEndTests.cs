using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.AnalyticsService.Infrastructure;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.AnalyticsService.IntegrationTests.Fixtures;

namespace OrderHub.AnalyticsService.IntegrationTests.Messaging;

/// <summary>
/// Faz 4 Adım 4c-2 — <b>gerçek Kafka</b> OrderEventsConsumer happy-path. Created→Confirmed→Paid
/// (type-header'lı, 4b producer formatı) gerçek topic'e produce edilir → consumer sırayla tüketip OrderProjection'ı
/// günceller (status=Paid). Ayrıca offset commit-after kanıtı: işleme sonrası aynı group'ta yeni consumer JOIN
/// edince hiçbir okunmamış mesaj görmez (DB önce, offset sonra). İdempotency/revenue 4c-3'te.
/// </summary>
[Collection(AnalyticsKafkaCollection.Name)]
public sealed class OrderEventsConsumerEndToEndTests(AnalyticsSqlServerContainerFixture sql, KafkaContainerFixture kafka)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly AnalyticsSqlServerContainerFixture _sql = sql;

    [Fact]
    public async Task Consumer_AppliesCreatedConfirmedPaidInOrder_ThenCommitsOffsetAfterProcessing()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";
        var paidAtUtc = DateTime.UtcNow;

        // Aynı key (OrderId) → aynı partition → sıralı tüketim (4b ordering).
        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = customerId, Amount = 150m, Currency = "TRY",
        });
        await ProduceAsync(new OrderConfirmedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = customerId, Amount = 150m, Currency = "TRY",
        });
        await ProduceAsync(new OrderPaidIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc, OrderId = orderId,
        });

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            // ★ consume→apply: 3 event sırayla → projection Paid + paid_at + total/customer.
            var projection = await WaitForStatusAsync(orderId, OrderProjectionStatus.Paid, TimeSpan.FromSeconds(60));
            projection.CustomerId.Should().Be(customerId);
            projection.Total.Should().Be(150m);
            projection.PaidAtUtc.Should().NotBeNull();
            projection.PaidAtUtc!.Value.Should().BeCloseTo(paidAtUtc, TimeSpan.FromSeconds(1));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None); // Close → final commit + group leave.
        }

        // ★ offset commit-after: aynı group'ta rejoin eden consumer committed offset'ten okur → HİÇBİR mesaj görmez.
        // Commit olmasaydı (offset ilerlememiş) earliest'ten 3 event'i tekrar okurdu → leftower != null olurdu.
        var leftover = ConsumeOneRemaining(groupId, TimeSpan.FromSeconds(10));
        leftover.Should().BeNull("DB önce + offset SONRA commit → rejoin eden consumer okunmamış mesaj görmez");

        await CleanupAsync(orderId);
    }

    [Fact]
    public async Task DuplicateOrderPaid_DedupPreventsDoubleRevenue_AndStampsInboxOnce()
    {
        var orderId = Guid.NewGuid();
        var sentinelOrderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";
        var paidAtUtc = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc); // diğer testlerle çakışmayan gün.
        var paidDate = DateOnly.FromDateTime(paidAtUtc);
        var paidEvent = new OrderPaidIntegrationEvent { Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc, OrderId = orderId };

        // Created → Confirmed → Paid → ★ DUPLICATE Paid (aynı eventId) → sentinel (B). Aynı topic, tek partition → sıralı.
        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc.AddMinutes(-2), OrderId = orderId,
            CustomerId = customerId, Amount = 150m, Currency = "TRY",
        });
        await ProduceAsync(new OrderConfirmedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc.AddMinutes(-1), OrderId = orderId,
            CustomerId = customerId, Amount = 150m, Currency = "TRY",
        });
        await ProduceAsync(paidEvent);
        await ProduceAsync(paidEvent); // ★ DUPLICATE (aynı Id).
        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc.AddMinutes(5), OrderId = sentinelOrderId,
            CustomerId = Guid.NewGuid(), Amount = 99m, Currency = "TRY",
        });

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            // Sentinel (son event) işlendi → ondan ÖNCEKİ duplicate Paid da işlendi (tek partition sırası).
            await WaitForProjectionExistsAsync(sentinelOrderId, TimeSpan.FromSeconds(60));

            // ★ Revenue TEK artış (ikiye katlanmadı): event-id dedup duplicate'i kesti.
            var revenue = await GetRevenueAsync(paidDate);
            revenue.TotalRevenue.Should().Be(150m, "duplicate OrderPaid event-id dedup → revenue ikiye katlanmaz");
            revenue.TotalOrders.Should().Be(1);

            // ★ InboxMessage (paidEventId) tam 1 kez → duplicate ikinci dedup kaydı yazmadı.
            (await CountInboxAsync(paidEvent.Id)).Should().Be(1);

            // OrderProjection tek + hâlâ Paid.
            var projection = await GetProjectionAsync(orderId);
            projection.Status.Should().Be(OrderProjectionStatus.Paid);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MultipleOrdersPaidSameDay_DailyRevenueAggregatesCorrectly()
    {
        var groupId = $"test-{Guid.NewGuid()}";
        var paidAtUtc = new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc); // diğer testlerden farklı gün.
        var paidDate = DateOnly.FromDateTime(paidAtUtc);

        foreach (var amount in new[] { 100m, 200m, 300m })
        {
            var lineOrderId = Guid.NewGuid();
            await ProduceAsync(new OrderCreatedIntegrationEvent
            {
                Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc.AddMinutes(-1), OrderId = lineOrderId,
                CustomerId = Guid.NewGuid(), Amount = amount, Currency = "TRY",
            });
            await ProduceAsync(new OrderPaidIntegrationEvent
            {
                Id = Guid.NewGuid(), OccurredOnUtc = paidAtUtc, OrderId = lineOrderId,
            });
        }

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var revenue = await WaitForRevenueAsync(paidDate, r => r.TotalOrders == 3, TimeSpan.FromSeconds(60));
            revenue.TotalRevenue.Should().Be(600m, "100 + 200 + 300");
            revenue.AvgOrderValue.Should().Be(200m, "600 / 3");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task ProduceAsync(OrderStreamEvent integrationEvent)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();

        var message = new Message<string, string>
        {
            Key = integrationEvent.PartitionKey,
            Value = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), PayloadOptions),
            Headers = new Headers
            {
                { KafkaMessageHeaders.MessageType, Encoding.UTF8.GetBytes(integrationEvent.GetType().FullName!) },
            },
        };

        await producer.ProduceAsync(integrationEvent.Topic, message);
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private string? ConsumeOneRemaining(string groupId, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(OrderStreamEvent.OrdersTopic);
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message is not null)
                {
                    return result.Message.Value;
                }
            }
        }
        finally
        {
            consumer.Close();
        }

        return null;
    }

    private async Task<OrderProjection> WaitForStatusAsync(Guid orderId, OrderProjectionStatus status, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using (var context = _sql.CreateContext())
            {
                var projection = await context.OrderProjections.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.OrderId == orderId, cts.Token);
                if (projection is not null && projection.Status == status)
                {
                    return projection;
                }
            }

            await Task.Delay(250, cts.Token);
        }
    }

    private ServiceProvider BuildProvider(string groupId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _sql.ConnectionString,
                ["Kafka:BootstrapServers"] = kafka.BootstrapServers,
                ["Kafka:GroupId"] = groupId,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private async Task WaitForProjectionExistsAsync(Guid orderId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using (var context = _sql.CreateContext())
            {
                if (await context.OrderProjections.AsNoTracking().AnyAsync(p => p.OrderId == orderId, cts.Token))
                {
                    return;
                }
            }

            await Task.Delay(250, cts.Token);
        }
    }

    private async Task<DailyRevenueProjection> WaitForRevenueAsync(
        DateOnly date, Func<DailyRevenueProjection, bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using (var context = _sql.CreateContext())
            {
                var revenue = await context.DailyRevenueProjections.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Date == date, cts.Token);
                if (revenue is not null && predicate(revenue))
                {
                    return revenue;
                }
            }

            await Task.Delay(250, cts.Token);
        }
    }

    private async Task<DailyRevenueProjection> GetRevenueAsync(DateOnly date)
    {
        await using var context = _sql.CreateContext();
        return await context.DailyRevenueProjections.AsNoTracking().SingleAsync(r => r.Date == date);
    }

    private async Task<OrderProjection> GetProjectionAsync(Guid orderId)
    {
        await using var context = _sql.CreateContext();
        return await context.OrderProjections.AsNoTracking().SingleAsync(p => p.OrderId == orderId);
    }

    private async Task<int> CountInboxAsync(Guid eventId)
    {
        await using var context = _sql.CreateContext();
        return await context.InboxMessages.AsNoTracking().CountAsync(m => m.MessageId == eventId);
    }

    private async Task CleanupAsync(Guid orderId)
    {
        await using var context = _sql.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OrderProjections WHERE OrderId = {orderId}");
    }
}
