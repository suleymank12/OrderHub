using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Messaging;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Faz 5 5d-5a — saga → OrderService komut consumer'ları (ConfirmOrder/MarkOrderPaid/ShipOrder), <b>broker'sız</b>
/// MassTransit in-memory harness (PaymentResultConsumerTests pattern). Gerçek production DI + gerçek SQL; order
/// uygun state'te seed edilir, komut integration event'i publish edilir, aggregate geçişi doğrulanır. ★ Bu adımda
/// consumer'lar DORMANT (saga OrderPlaced akmadan command göndermez) — testler consumer'ları doğrudan tetikler.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SagaCommandConsumerTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task ConfirmOrderConsumer_PendingOrder_TransitionsToConfirmed()
    {
        var orderId = await SeedOrderAsync(OrderLifecycle.Pending);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(ConfirmOrder(orderId));
                (await harness.Consumed.Any<ConfirmOrderIntegrationEvent>()).Should().BeTrue();

                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Confirmed);
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task ConfirmOrderConsumer_DeliveredTwice_IsIdempotent_ConfirmedOnce()
    {
        var orderId = await SeedOrderAsync(OrderLifecycle.Pending);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                var message = ConfirmOrder(orderId);
                await harness.Bus.Publish(message);
                (await harness.Consumed.Any<ConfirmOrderIntegrationEvent>()).Should().BeTrue();

                await harness.Bus.Publish(message); // at-least-once redelivery
                await WaitForConsumedCountAsync<ConfirmOrderIntegrationEvent>(harness, 2);

                // Aggregate guard: ikinci Confirm no-op → hâlâ Confirmed (exception yok).
                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Confirmed);
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task MarkOrderPaidConsumer_ConfirmedOrder_TransitionsToPaid()
    {
        var orderId = await SeedOrderAsync(OrderLifecycle.Confirmed);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(MarkOrderPaid(orderId));
                (await harness.Consumed.Any<MarkOrderPaidIntegrationEvent>()).Should().BeTrue();

                var consumed = harness.Consumed.Select<MarkOrderPaidIntegrationEvent>().Single();
                consumed.Exception.Should().BeNull("Confirmed sipariş → handler Success → ack (throw yok)");
                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Paid);
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task MarkOrderPaidConsumer_PendingOrder_ThrowsToRetry_OrderStaysPending()
    {
        // ★ Karar D kanıtı: ConfirmOrder henüz işlenmedi (order Pending) → handler retryable failure →
        // consumer THROW (→ MassTransit retry). Order Pending kalır (MarkPaid uygulanmaz).
        var orderId = await SeedOrderAsync(OrderLifecycle.Pending);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(MarkOrderPaid(orderId));
                (await harness.Consumed.Any<MarkOrderPaidIntegrationEvent>()).Should().BeTrue();

                var consumed = harness.Consumed.Select<MarkOrderPaidIntegrationEvent>().First();
                consumed.Exception.Should().NotBeNull("Pending → consumer throw (retry tetikler — Karar D)");
                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Pending, "MarkPaid uygulanmadı");
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task ShipOrderConsumer_PaidOrder_TransitionsToShipped()
    {
        var orderId = await SeedOrderAsync(OrderLifecycle.Paid);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(ShipOrder(orderId));
                (await harness.Consumed.Any<ShipOrderIntegrationEvent>()).Should().BeTrue();

                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Shipped);
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task CancelOrderConsumer_PendingOrder_TransitionsToCancelled()
    {
        var orderId = await SeedOrderAsync(OrderLifecycle.Pending);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(CancelOrder(orderId));
                (await harness.Consumed.Any<CancelOrderIntegrationEvent>()).Should().BeTrue();

                var consumed = harness.Consumed.Select<CancelOrderIntegrationEvent>().Single();
                consumed.Exception.Should().BeNull("iptal forward telafi → ack (throw yok)");
                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Cancelled);
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    [Fact]
    public async Task CancelOrderConsumer_PaidOrder_EdgeFailureAckedNoThrow_StaysPaid()
    {
        // ★ Edge: telafi Paid'e ULAŞMAZ (forward-only) ama consumer savunmacı: Paid → handler Conflict failure →
        // consumer ack'ler (throw YOK; iptal retry gerektirmez). Order Paid kalır (iptal uygulanmaz).
        var orderId = await SeedOrderAsync(OrderLifecycle.Paid);
        try
        {
            await using var provider = BuildProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(CancelOrder(orderId));
                (await harness.Consumed.Any<CancelOrderIntegrationEvent>()).Should().BeTrue();

                var consumed = harness.Consumed.Select<CancelOrderIntegrationEvent>().Single();
                consumed.Exception.Should().BeNull("Paid edge → Failure ack (retry yok, throw yok)");
                (await LoadOrderAsync(orderId)).Status.Should().Be(OrderStatus.Paid, "iptal uygulanmadı");
            }
            finally { await harness.Stop(); }
        }
        finally { await CleanupOrderAsync(orderId); }
    }

    private enum OrderLifecycle { Pending, Confirmed, Paid }

    private static ConfirmOrderIntegrationEvent ConfirmOrder(Guid orderId) =>
        new() { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId };

    private static MarkOrderPaidIntegrationEvent MarkOrderPaid(Guid orderId) =>
        new() { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId };

    private static ShipOrderIntegrationEvent ShipOrder(Guid orderId) =>
        new() { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId };

    private static CancelOrderIntegrationEvent CancelOrder(Guid orderId) =>
        new() { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, Reason = "stock_unavailable" };

    private async Task<Guid> SeedOrderAsync(OrderLifecycle lifecycle)
    {
        await using var context = fixture.CreateContext();
        var order = OrderTestData.NewOrder();
        if (lifecycle is OrderLifecycle.Confirmed or OrderLifecycle.Paid)
        {
            order.Confirm();
        }

        if (lifecycle is OrderLifecycle.Paid)
        {
            order.MarkPaid();
        }

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
        services.RemoveAll<IHostedService>();

        services.AddMassTransitTestHarness(busConfigurator =>
        {
            busConfigurator.AddConsumer<ConfirmOrderConsumer>();
            busConfigurator.AddConsumer<MarkOrderPaidConsumer>();
            busConfigurator.AddConsumer<ShipOrderConsumer>();
            busConfigurator.AddConsumer<CancelOrderConsumer>();
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
