using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderHub.Contracts.Payments;
using OrderHub.EventBus;
using OrderHub.Outbox;
using OrderHub.PaymentService.Application;
using OrderHub.PaymentService.Application.Payments.Configuration;
using OrderHub.PaymentService.Domain.Payments;
using OrderHub.PaymentService.Infrastructure;
using OrderHub.PaymentService.Infrastructure.Messaging;
using OrderHub.PaymentService.Infrastructure.Persistence;
using OrderHub.PaymentService.IntegrationTests.Fixtures;

namespace OrderHub.PaymentService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3c-2 — <see cref="ProcessPaymentIntegrationEventConsumer"/> davranışı, <b>broker'sız</b>
/// MassTransit in-memory test harness ile (ApiTestFactory yok). Gerçek <c>AddInfrastructure</c>/<c>AddApplication</c>
/// DI'ı + SQL container kullanılır; yalnız transport in-memory'ye çevrilir. OutboxProcessor başlatılmaz
/// (BuildServiceProvider hosted service çalıştırmaz, yalnız harness.Start) → outbox satırı yazılır ama
/// publish edilmez (ProcessedOnUtc null ile incelenebilir).
/// </summary>
[Collection(PaymentDatabaseCollection.Name)]
public sealed class ProcessPaymentConsumerTests(PaymentSqlServerContainerFixture fixture)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Consume_ProviderSucceeds_PersistsSucceededPaymentAndWritesPaymentSucceededOutbox()
    {
        var orderId = Guid.NewGuid();
        await using var provider = BuildProvider(successRatePercent: 100);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            await harness.Bus.Publish(NewProcessPayment(orderId));
            (await harness.Consumed.Any<ProcessPaymentIntegrationEvent>()).Should().BeTrue();

            using var scope = provider.CreateScope();
            var payment = await scope.ServiceProvider.GetRequiredService<PaymentDbContext>()
                .Payments.SingleAsync(p => p.OrderId == orderId);
            payment.Status.Should().Be(PaymentStatus.Succeeded);
            payment.ExternalTransactionId.Should().NotBeNullOrWhiteSpace();

            // Outbox'ta PaymentSucceededIntegrationEvent: işlenmemiş (ProcessedOnUtc null), payload doğru.
            var row = await SingleOutboxRowAsync<PaymentSucceededIntegrationEvent>(scope, orderId);
            row.ProcessedOnUtc.Should().BeNull("processor başlatılmadı → satır yazıldı, publish edilmedi");
            var payload = Deserialize<PaymentSucceededIntegrationEvent>(row.Payload);
            payload.OrderId.Should().Be(orderId);
            payload.ExternalTransactionId.Should().Be(payment.ExternalTransactionId);
            // Id == kaynak EventId invariant'ı: registry yazarken doğruladı; satırın varlığı = fail-fast etmedi.
            payload.Id.Should().Be(row.Id);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_ProviderFails_PersistsFailedPaymentAndWritesPaymentFailedOutbox()
    {
        var orderId = Guid.NewGuid();
        await using var provider = BuildProvider(successRatePercent: 0);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            await harness.Bus.Publish(NewProcessPayment(orderId));
            (await harness.Consumed.Any<ProcessPaymentIntegrationEvent>()).Should().BeTrue();

            using var scope = provider.CreateScope();
            var payment = await scope.ServiceProvider.GetRequiredService<PaymentDbContext>()
                .Payments.SingleAsync(p => p.OrderId == orderId);
            payment.Status.Should().Be(PaymentStatus.Failed);
            payment.ExternalTransactionId.Should().BeNull();

            var row = await SingleOutboxRowAsync<PaymentFailedIntegrationEvent>(scope, orderId);
            row.ProcessedOnUtc.Should().BeNull();
            var payload = Deserialize<PaymentFailedIntegrationEvent>(row.Payload);
            payload.OrderId.Should().Be(orderId);
            payload.Reason.Should().NotBeNullOrWhiteSpace();
            payload.Id.Should().Be(row.Id);
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static ProcessPaymentIntegrationEvent NewProcessPayment(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OccurredOnUtc = DateTime.UtcNow,
        OrderId = orderId,
        CustomerId = Guid.NewGuid(),
        Amount = 150m,
        Currency = "TRY",
    };

    // Belirli sipariş için verilen integration tipinin tek outbox satırını bulur (paylaşılan container'da izolasyon).
    private static async Task<OutboxMessage> SingleOutboxRowAsync<TEvent>(IServiceScope scope, Guid orderId)
        where TEvent : IIntegrationEvent
    {
        var rows = await scope.ServiceProvider.GetRequiredService<IOutboxDbContext>()
            .OutboxMessages.ToListAsync();

        return rows.Single(m =>
            m.Type == typeof(TEvent).AssemblyQualifiedName &&
            OrderIdOf(m.Payload) == orderId);
    }

    private static Guid OrderIdOf(string payload) =>
        JsonSerializer.Deserialize<OrderIdProbe>(payload, PayloadOptions)!.OrderId;

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, PayloadOptions)!;

    // Gerçek production DI; yalnız transport in-memory harness'e çevrilir + mock provider oranı set edilir.
    private ServiceProvider BuildProvider(int successRatePercent)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Mock provider oranını deterministik set et (default 100; %0 için override).
        services.AddSingleton<IOptions<MockPaymentOptions>>(
            Options.Create(new MockPaymentOptions { SuccessRatePercent = successRatePercent }));

        // Production RabbitMQ MassTransit'i kaldır → in-memory harness + consumer (broker'sız).
        RemoveMassTransitRegistrations(services);
        services.AddMassTransitTestHarness(x => x.AddConsumer<ProcessPaymentIntegrationEventConsumer>());

        return services.BuildServiceProvider();
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

    private sealed record OrderIdProbe(Guid OrderId);
}
