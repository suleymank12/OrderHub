using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Payments;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Messaging;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3c-4 — Order yönü <b>gerçek RabbitMQ</b> fidelity testi. In-memory harness (3c-3) mantığı zaten
/// doğruladı; bu test gerçek transport'ta serialization/routing/consumer-wiring'in bozulmadığını kanıtlar.
/// Gerçek bus (3b-2 smoke pattern'i: <c>AddMassTransit</c> + <c>UsingRabbitMq</c> + manuel
/// <c>IBusControl.StartAsync</c>). Order önce Confirmed seed edilir (<c>fixture.CreateContext</c> →
/// Hangfire/outbox tetiklenmez). Tek test (yavaş).
/// </summary>
[Collection(EndToEndCollection.Name)]
public sealed class PaymentResultEndToEndTests(
    SqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    [Fact]
    public async Task PaymentSucceeded_OverRealBroker_TransitionsOrderToPaid()
    {
        var orderId = await SeedConfirmedOrderAsync();

        await using var provider = BuildProvider();
        var busControl = provider.GetRequiredService<IBusControl>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await busControl.StartAsync(timeout.Token);

        try
        {
            // Gerçek broker kanıtı: loopback değil → RabbitMQ transport.
            busControl.Address.Scheme.Should().Be("rabbitmq");

            await busControl.Publish(
                new PaymentSucceededIntegrationEvent
                {
                    Id = Guid.NewGuid(),
                    OccurredOnUtc = DateTime.UtcNow,
                    OrderId = orderId,
                    ExternalTransactionId = "MOCK-TX-E2E",
                },
                timeout.Token);

            // Consumer gerçek broker'dan tüketip order'ı Paid yapana kadar bekle (poll).
            var order = await WaitForOrderStatusAsync(orderId, OrderStatus.Paid, timeout.Token);
            order.PaidAtUtc.Should().NotBeNull();
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    private async Task<Guid> SeedConfirmedOrderAsync()
    {
        await using var context = sql.CreateContext(); // NullPublisher + OutboxInterceptor yok → yan etki yok.
        var order = OrderTestData.NewOrder();
        order.Confirm();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Order> WaitForOrderStatusAsync(
        Guid orderId, OrderStatus expected, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using (var context = sql.CreateContext())
            {
                var order = await context.Orders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
                if (order is not null && order.Status == expected)
                {
                    return order;
                }
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private ServiceProvider BuildProvider()
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

        RemoveMassTransitRegistrations(services); // prod (localhost) MassTransit → çıkar, gerçek container ile yeniden kur.
        services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.AddConsumer<PaymentSucceededIntegrationEventConsumer>();
            busConfigurator.AddConsumer<PaymentFailedIntegrationEventConsumer>();
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
