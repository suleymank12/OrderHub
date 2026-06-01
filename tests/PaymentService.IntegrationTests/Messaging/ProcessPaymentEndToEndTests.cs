using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderHub.Contracts.Payments;
using OrderHub.PaymentService.Application;
using OrderHub.PaymentService.Application.Payments.Configuration;
using OrderHub.PaymentService.Domain.Payments;
using OrderHub.PaymentService.Infrastructure;
using OrderHub.PaymentService.Infrastructure.Messaging;
using OrderHub.PaymentService.Infrastructure.Persistence;
using OrderHub.PaymentService.IntegrationTests.Fixtures;

namespace OrderHub.PaymentService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3c-4 — Payment yönü <b>gerçek RabbitMQ</b> fidelity testi. In-memory harness (3c-2) zaten
/// happy/failure mantığını doğruladı; bu test gerçek transport'ta serialization/routing/consumer-wiring'in
/// bozulmadığını kanıtlar. Gerçek bus (3b-2 smoke pattern'i: <c>AddMassTransit</c> + <c>UsingRabbitMq</c> +
/// manuel <c>IBusControl.StartAsync</c>; ITestHarness değil → IHostedService karmaşası yok). Tek test (yavaş).
/// </summary>
[Collection(PaymentEndToEndCollection.Name)]
public sealed class ProcessPaymentEndToEndTests(
    PaymentSqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    [Fact]
    public async Task ProcessPaymentIntegrationEvent_OverRealBroker_PersistsSucceededPayment()
    {
        var orderId = Guid.NewGuid();
        await using var provider = BuildProvider(successRatePercent: 100);
        var busControl = provider.GetRequiredService<IBusControl>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await busControl.StartAsync(timeout.Token);

        try
        {
            // Gerçek broker kanıtı: loopback (in-memory) değil → MassTransit RabbitMQ transport.
            busControl.Address.Scheme.Should().Be("rabbitmq");

            await busControl.Publish(
                new ProcessPaymentIntegrationEvent
                {
                    Id = Guid.NewGuid(),
                    OccurredOnUtc = DateTime.UtcNow,
                    OrderId = orderId,
                    CustomerId = Guid.NewGuid(),
                    Amount = 150m,
                    Currency = "TRY",
                },
                timeout.Token);

            // Consumer gerçek broker'dan tüketip Payment'ı yazana kadar bekle (poll).
            var payment = await WaitForPaymentAsync(provider, orderId, timeout.Token);
            payment.Status.Should().Be(PaymentStatus.Succeeded);
            payment.ExternalTransactionId.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<Payment> WaitForPaymentAsync(
        IServiceProvider provider, Guid orderId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var scope = provider.CreateScope())
            {
                var payment = await scope.ServiceProvider.GetRequiredService<PaymentDbContext>()
                    .Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
                if (payment is not null)
                {
                    return payment;
                }
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    // Gerçek production DI; prod RabbitMQ kaydı çıkarılıp test-özel UsingRabbitMq (container) eklenir.
    // BuildServiceProvider hosted service başlatmaz; bus manuel start → RecurringJobRegistrar/OutboxProcessor
    // (varsa) dormant kalır, yalnız bus + consumer endpoint çalışır.
    private ServiceProvider BuildProvider(int successRatePercent)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IOptions<MockPaymentOptions>>(
            Options.Create(new MockPaymentOptions { SuccessRatePercent = successRatePercent }));

        RemoveMassTransitRegistrations(services); // prod (localhost) MassTransit → çıkar, gerçek container ile yeniden kur.
        services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.AddConsumer<ProcessPaymentIntegrationEventConsumer>();
            busConfigurator.UsingRabbitMq((context, rabbitMq) =>
            {
                rabbitMq.Host(new Uri(rabbit.ConnectionString));
                rabbitMq.ConfigureEndpoints(context);
            });
        });

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
}
