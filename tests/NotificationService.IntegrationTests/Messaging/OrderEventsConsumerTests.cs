using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderHub.NotificationService.Domain.Orders;
using OrderHub.NotificationService.Infrastructure;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.NotificationService.IntegrationTests.Fixtures;

namespace OrderHub.NotificationService.IntegrationTests.Messaging;

/// <summary>
/// Faz 5 Adım 5f-1 — <b>gerçek Kafka</b> OrderEventsConsumer happy-path. Created→Confirmed→Paid / Cancelled
/// (type-header'lı, 4b producer formatı) gerçek topic'e produce edilir → consumer tüketip OrderProjection'ı
/// günceller. Ayrıca: offset commit-after kanıtı, inbox-dedup, self-heal (topic-yok penceresi).
/// AnalyticsService consumer testinin notification consumer karşılığı (Revenue YOK; daha sade).
/// </summary>
[Collection(NotificationKafkaCollection.Name)]
public sealed class OrderEventsConsumerTests(NotificationSqlServerContainerFixture sql, KafkaContainerFixture kafka)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Consumer_AppliesCreatedConfirmedPaid_ThenCommitsOffsetAfterProcessing()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";

        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = customerId, Amount = 200m, Currency = "TRY",
        });
        await ProduceAsync(new OrderConfirmedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = customerId, Amount = 200m, Currency = "TRY",
        });
        await ProduceAsync(new OrderPaidIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
        });

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            // ★ consume→apply: 3 event sırayla → projection Paid.
            var projection = await WaitForStatusAsync(orderId, OrderProjectionStatus.Paid, TimeSpan.FromSeconds(60));
            projection.CustomerId.Should().Be(customerId);
            projection.Total.Should().Be(200m);
            projection.Currency.Should().Be("TRY");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        // ★ offset commit-after: rejoin eden aynı group consumer okunmamış mesaj görmez.
        var leftover = ConsumeOneRemaining(groupId, TimeSpan.FromSeconds(10));
        leftover.Should().BeNull("DB önce + offset SONRA commit → rejoin eden consumer okunmamış mesaj görmez");

        await CleanupAsync(orderId);
    }

    [Fact]
    public async Task Consumer_OrderCancelled_SetsStatusToCancelled()
    {
        var orderId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";

        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = Guid.NewGuid(), Amount = 100m, Currency = "TRY",
        });
        await ProduceAsync(new OrderCancelledIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, Reason = "Customer request",
        });

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var projection = await WaitForStatusAsync(orderId, OrderProjectionStatus.Cancelled, TimeSpan.FromSeconds(60));
            projection.Status.Should().Be(OrderProjectionStatus.Cancelled);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DuplicateOrderCreated_DedupPreventsDoubleApply_AndStampsInboxOnce()
    {
        var orderId = Guid.NewGuid();
        var sentinelOrderId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";
        var createdEvent = new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
            CustomerId = Guid.NewGuid(), Amount = 150m, Currency = "TRY",
        };

        // Created → DUPLICATE Created (aynı eventId) → sentinel. Tek partition → sıralı.
        await ProduceAsync(createdEvent);
        await ProduceAsync(createdEvent); // ★ DUPLICATE (aynı Id).
        await ProduceAsync(new OrderCreatedIntegrationEvent
        {
            Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = sentinelOrderId,
            CustomerId = Guid.NewGuid(), Amount = 99m, Currency = "TRY",
        });

        await using var provider = BuildProvider(groupId);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            // Sentinel işlendi → duplicate da geçti (tek partition sırası).
            await WaitForProjectionExistsAsync(sentinelOrderId, TimeSpan.FromSeconds(60));

            // ★ InboxMessage tam 1 kez → duplicate ikinci dedup kaydı yazmadı.
            (await CountInboxAsync(createdEvent.Id)).Should().Be(1, "event-id dedup ikinci kaydı engelledi");

            // Projection sadece bir kez oluştu (Status hâlâ Created).
            var projection = await GetProjectionAsync(orderId);
            projection.Status.Should().Be(OrderProjectionStatus.Created);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Consumer_StartedBeforeTopicExists_SurvivesAndConsumesAfterTopicAppears()
    {
        var orderId = Guid.NewGuid();
        var groupId = $"test-{Guid.NewGuid()}";

        await using var provider = BuildProvider(groupId);
        var hostedService = provider.GetServices<IHostedService>().Single();
        var backgroundService = (BackgroundService)hostedService;

        // ★ Topic HENÜZ YOK iken başlat.
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            // ★ Topic-yok penceresi: Consume() UnknownTopicOrPart fırlatır. Fix ile → Warning + backoff + continue.
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            backgroundService.ExecuteTask.Should().NotBeNull();
            backgroundService.ExecuteTask!.IsFaulted.Should().BeFalse(
                "topic yokken Consume() UnknownTopicOrPart fırlatır; consumer bunu yutup beklemeli, ölmemeli");
            backgroundService.ExecuteTask.IsCompleted.Should().BeFalse("consumer hâlâ consume döngüsünde olmalı");

            // ★ Şimdi produce et → topic oluşur. Canlı abonelik fetch'e başlar (self-heal).
            await ProduceAsync(new OrderCreatedIntegrationEvent
            {
                Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId,
                CustomerId = Guid.NewGuid(), Amount = 75m, Currency = "TRY",
            });

            // ★ Self-heal kanıtı: topic GEÇ oluşmasına rağmen consumer ayakta kalıp tüketti.
            var projection = await WaitForStatusAsync(orderId, OrderProjectionStatus.Created, TimeSpan.FromSeconds(60));
            projection.Total.Should().Be(75m);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        await CleanupAsync(orderId);
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

    private ServiceProvider BuildProvider(string groupId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
                ["Kafka:BootstrapServers"] = kafka.BootstrapServers,
                ["Kafka:GroupId"] = groupId,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private async Task<OrderProjection> WaitForStatusAsync(Guid orderId, OrderProjectionStatus status, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using (var context = sql.CreateContext())
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

    private async Task WaitForProjectionExistsAsync(Guid orderId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using (var context = sql.CreateContext())
            {
                if (await context.OrderProjections.AsNoTracking().AnyAsync(p => p.OrderId == orderId, cts.Token))
                {
                    return;
                }
            }

            await Task.Delay(250, cts.Token);
        }
    }

    private async Task<OrderProjection> GetProjectionAsync(Guid orderId)
    {
        await using var context = sql.CreateContext();
        return await context.OrderProjections.AsNoTracking().SingleAsync(p => p.OrderId == orderId);
    }

    private async Task<int> CountInboxAsync(Guid eventId)
    {
        await using var context = sql.CreateContext();
        return await context.InboxMessages.AsNoTracking().CountAsync(m => m.MessageId == eventId);
    }

    private async Task CleanupAsync(Guid orderId)
    {
        await using var context = sql.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OrderProjections WHERE OrderId = {orderId}");
    }
}
