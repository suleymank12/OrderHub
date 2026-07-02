using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus;
using OrderHub.EventBus.Kafka;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;
using OrderHub.Outbox;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// <b>Gerçek Kafka</b> producer + 1:N <b>canlı</b> kanıt (ADR-0006), Faz 5 5d-5b cutover ile güncel. Bir OrderCreated
/// işlenince outbox processor 1:N fan-out eder: <c>OrderPlacedIntegrationEvent</c> (IRabbitMqEvent, saga trigger) →
/// RabbitMQ publisher (capturing stub) + <c>OrderCreatedIntegrationEvent</c> (IKafkaEvent) → gerçek Kafka topic.
/// Tek dış broker = Kafka (RabbitMQ tarafı routing'i izole etmek için stub → deterministik). Processor manuel
/// start edilir. Production wiring (AddInfrastructure → AddKafkaRouting); yalnız RabbitMQ keyed publisher + IProducer override.
/// </summary>
[Collection(KafkaEndToEndCollection.Name)]
public sealed class OrderStreamProducerEndToEndTests(SqlServerContainerFixture sql, KafkaContainerFixture kafka)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreatedOrder_ProcessorFansOut_OrderPlacedToRabbitMq_AndOrderCreatedToRealKafka()
    {
        var capture = new CapturingPublisher();
        var orderId = Guid.Empty;
        var createdEventId = Guid.Empty;
        await using var provider = BuildProvider(capture);

        var processor = provider.GetServices<IHostedService>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var order = OrderTestData.NewOrder(); // yalnız OrderCreated.
                orderId = order.Id;
                createdEventId = order.DomainEvents.OfType<OrderCreated>().Single().EventId;
                context.Orders.Add(order);
                await context.SaveChangesAsync(); // outbox: OrderCreated(Kafka, Ordinal 0) + OrderPlaced(RabbitMQ, Ordinal 1)
            }

            // ★ 1:N canlı — RabbitMQ tarafı: routing OrderPlaced'i RabbitMQ publisher'a yönlendirdi (capture, saga trigger).
            await WaitUntilAsync(
                () => capture.Captured.OfType<OrderPlacedIntegrationEvent>().Any(e => e.OrderId == orderId),
                TimeSpan.FromSeconds(30));

            // ★ 1:N canlı — Kafka tarafı: aynı OrderCreated GERÇEK Kafka topic'ine düştü (header tip = OrderCreated).
            var kafkaEvent = ConsumeOrderCreated(kafka.BootstrapServers, orderId, TimeSpan.FromSeconds(60));
            kafkaEvent.Should().NotBeNull("OrderCreated Kafka'ya publish edilmeli (1:N fan-out canlı)");
            kafkaEvent!.OrderId.Should().Be(orderId);
            kafkaEvent.Id.Should().Be(createdEventId, "Kafka event Id == OrderCreated.EventId (uçtan uca dedup)");

            // Bütünlük: OrderPlaced (RabbitMQ) ile OrderCreated (Kafka) AYNI EventId'den fan-out etti.
            capture.Captured.OfType<OrderPlacedIntegrationEvent>().Single(e => e.OrderId == orderId)
                .Id.Should().Be(createdEventId);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
            await CleanupAsync();
        }
    }

    private static OrderCreatedIntegrationEvent? ConsumeOrderCreated(string bootstrapServers, Guid orderId, TimeSpan timeout)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(OrderStreamEvent.OrdersTopic);
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is null)
                {
                    continue;
                }

                // Tip header'dan dispatch (OrderCreated ve OrderConfirmed aynı JSON şekline sahip → header şart).
                if (!result.Message.Headers.TryGetLastBytes(KafkaMessageHeaders.MessageType, out var typeBytes) ||
                    Encoding.UTF8.GetString(typeBytes) != typeof(OrderCreatedIntegrationEvent).FullName)
                {
                    continue;
                }

                var integrationEvent = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(result.Message.Value, PayloadOptions);
                if (integrationEvent?.OrderId == orderId)
                {
                    return integrationEvent;
                }
            }
        }
        finally
        {
            consumer.Close();
        }

        return null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(100, cts.Token);
        }
    }

    // Gerçek production DI (AddInfrastructure → AddKafkaRouting). RabbitMQ tarafı capturing stub'a, Kafka tarafı
    // gerçek container'a yönlendirilir (keyed override). Dispatch interceptor NullPublisher → MediatR/Hangfire devre dışı.
    private ServiceProvider BuildProvider(CapturingPublisher capture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IPublisher>(NullPublisher.Instance);

        RemoveMassTransitRegistrations(services);
        services.RemoveAll<IHostedService>(); // RecurringJobRegistrar (Hangfire) + processor + MT bus → çıkar.

        services.AddKeyedSingleton<IIntegrationEventPublisher>(EventBusPublisherKeys.RabbitMq, capture);

        services.RemoveAll<IProducer<string, string>>();
        services.AddSingleton<IProducer<string, string>>(new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build());

        services.AddOutboxProcessor(options => options.PollingInterval = TimeSpan.FromMilliseconds(200));

        return services.BuildServiceProvider();
    }

    private async Task CleanupAsync()
    {
        await using var context = sql.CreateContext(); // izole collection DB → tabloları temizlemek güvenli.
        await context.Database.ExecuteSqlRawAsync("DELETE FROM OutboxMessages");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Orders");
    }

    private static void RemoveMassTransitRegistrations(ServiceCollection services)
    {
        var descriptors = services
            .Where(d =>
                IsMassTransitType(d.ServiceType) ||
                IsMassTransitType(d.ImplementationType) ||
                IsMassTransitType(d.ImplementationInstance?.GetType()))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsMassTransitType(Type? type) =>
        type?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;

    private sealed class CapturingPublisher : IIntegrationEventPublisher
    {
        private readonly object _gate = new();
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured
        {
            get
            {
                lock (_gate)
                {
                    return _captured.ToList();
                }
            }
        }

        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _captured.Add(integrationEvent);
            }

            return Task.CompletedTask;
        }
    }
}
