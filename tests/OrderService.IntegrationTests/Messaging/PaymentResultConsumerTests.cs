using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Payments;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Messaging;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3c-3 — OrderService'in PaymentSucceeded/Failed tüketicileri, <b>broker'sız</b> MassTransit
/// in-memory harness ile (ApiTestFactory'ye dokunulmaz). Order önce gerçek bir Confirmed sipariş olarak
/// seed edilir (<c>fixture.CreateContext</c> → NullPublisher + OutboxInterceptor yok → Hangfire/outbox
/// tetiklenmez), sonra integration event publish edilir. Seed edilen siparişler finally'de temizlenir
/// (paylaşılan container izolasyonu).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PaymentResultConsumerTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task PaymentSucceeded_ConfirmedOrder_TransitionsToPaid()
    {
        var orderId = await SeedConfirmedOrderAsync();
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(NewPaymentSucceeded(orderId));
                (await harness.Consumed.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();

                var order = await LoadOrderAsync(orderId);
                order.Status.Should().Be(OrderStatus.Paid);
                order.PaidAtUtc.Should().NotBeNull();
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            await CleanupOrderAsync(orderId);
        }
    }

    [Fact]
    public async Task PaymentFailed_ConfirmedOrder_TransitionsToCancelledWithPaymentFailedReason()
    {
        var orderId = await SeedConfirmedOrderAsync();
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(new PaymentFailedIntegrationEvent
                {
                    Id = Guid.NewGuid(),
                    OccurredOnUtc = DateTime.UtcNow,
                    OrderId = orderId,
                    Reason = "declined by provider",
                });
                (await harness.Consumed.Any<PaymentFailedIntegrationEvent>()).Should().BeTrue();

                var order = await LoadOrderAsync(orderId);
                order.Status.Should().Be(OrderStatus.Cancelled);
                order.CancellationReason.Should().Be("payment_failed"); // OrderCancellationReasons.PaymentFailed (internal).
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            await CleanupOrderAsync(orderId);
        }
    }

    [Fact]
    public async Task PaymentSucceeded_PublishedTwice_IsIdempotent_OrderPaidOnce()
    {
        var orderId = await SeedConfirmedOrderAsync();
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                var message = NewPaymentSucceeded(orderId);

                await harness.Bus.Publish(message);
                (await harness.Consumed.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();
                var paidAtAfterFirst = (await LoadOrderAsync(orderId)).PaidAtUtc;
                paidAtAfterFirst.Should().NotBeNull();

                // At-least-once redelivery simülasyonu: aynı mesaj ikinci kez.
                await harness.Bus.Publish(message);
                await WaitForConsumedCountAsync<PaymentSucceededIntegrationEvent>(harness, 2);

                var order = await LoadOrderAsync(orderId);
                order.Status.Should().Be(OrderStatus.Paid);
                // İkinci publish no-op: MarkPaid çağrılmadı → PaidAtUtc DEĞİŞMEDİ (aggregate guard, throw yok).
                order.PaidAtUtc.Should().Be(paidAtAfterFirst);
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            await CleanupOrderAsync(orderId);
        }
    }

    [Fact]
    public async Task PaymentSucceeded_UnknownOrder_IsConsumedAndAckedWithoutException()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(NewPaymentSucceeded(Guid.NewGuid())); // DB'de order yok.
            (await harness.Consumed.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();

            // NotFound → handler Result.Failure → consumer log + ack (throw YOK → mesajda exception yok).
            var consumed = harness.Consumed.Select<PaymentSucceededIntegrationEvent>().Single();
            consumed.Exception.Should().BeNull();
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static PaymentSucceededIntegrationEvent NewPaymentSucceeded(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OccurredOnUtc = DateTime.UtcNow,
        OrderId = orderId,
        ExternalTransactionId = "MOCK-TX-1",
    };

    private async Task<Guid> SeedConfirmedOrderAsync()
    {
        // CreateContext: NullPublisher (dispatch no-op → Hangfire yok) + OutboxInterceptor yok (outbox satırı yok).
        await using var context = fixture.CreateContext();
        var order = OrderTestData.NewOrder();
        order.Confirm();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Order> LoadOrderAsync(Guid orderId)
    {
        await using var context = fixture.CreateContext();
        return await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    private async Task CleanupOrderAsync(Guid orderId)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {orderId}");
    }

    // Gerçek production DI; yalnız transport in-memory harness'e çevrilir + iki consumer kaydedilir.
    private ServiceProvider BuildProvider()
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

        RemoveMassTransitRegistrations(services);

        // Harness.Start tüm IHostedService'leri başlatır; RecurringJobRegistrar (Hangfire/IRecurringJobManager)
        // ve OutboxProcessor consumer testinde gerekmez ve Hangfire DI'ı yok → kaldır (harness kendi bus
        // hosted service'ini sonra ekler → korunur).
        services.RemoveAll<IHostedService>();

        services.AddMassTransitTestHarness(busConfigurator =>
        {
            busConfigurator.AddConsumer<PaymentSucceededIntegrationEventConsumer>();
            busConfigurator.AddConsumer<PaymentFailedIntegrationEventConsumer>();
        });

        return services.BuildServiceProvider();
    }

    private static async Task WaitForConsumedCountAsync<T>(ITestHarness harness, int expectedCount)
        where T : class
    {
        var count = 0;
        await foreach (var _ in harness.Consumed.SelectAsync<T>())
        {
            if (++count >= expectedCount)
            {
                return;
            }
        }
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
