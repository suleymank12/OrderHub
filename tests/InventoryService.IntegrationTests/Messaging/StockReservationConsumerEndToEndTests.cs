using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Inventory;
using OrderHub.InventoryService.Application;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Infrastructure;
using OrderHub.InventoryService.Infrastructure.Messaging;
using OrderHub.InventoryService.Infrastructure.Persistence;
using OrderHub.InventoryService.IntegrationTests.Fixtures;

namespace OrderHub.InventoryService.IntegrationTests.Messaging;

/// <summary>
/// Faz 5 5c — gerçek RabbitMQ broker ile consumer uçtan uca fidelity testi. Prod DI
/// (AddApplication + AddInfrastructure) üzerine prod MassTransit çıkarılıp container
/// UsingRabbitMq ile yeniden kurulur; BuildServiceProvider hosted service başlatmaz →
/// OutboxProcessor/RecurringJobRegistrar dormant, bus manuel start edilir. Outbox işlenmediğinden
/// OutboxMessages satırının VARLIĞI doğrulanır (publish edilmiş içerik değil).
/// </summary>
[Collection(InventoryEndToEndCollection.Name)]
public sealed class StockReservationConsumerEndToEndTests(
    InventorySqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    [Fact]
    public async Task ReserveStockConsumer_SufficientStock_CreatesPendingReservationAndOutboxRow()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var provider = BuildProvider();
        await SeedStockItemAsync(provider, productId, initialQuantity: 10);

        var busControl = provider.GetRequiredService<IBusControl>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await busControl.StartAsync(cts.Token);
        try
        {
            await busControl.Publish(new ReserveStockIntegrationEvent
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = orderId,
                ProductId = productId,
                Quantity = 3,
            }, cts.Token);

            await WaitForPendingReservationAsync(provider, productId, orderId, cts.Token);

            var stockItem = await LoadStockItemAsync(provider, productId, cts.Token);
            stockItem.AvailableQuantity.Should().Be(7);
            stockItem.Reservations.Should().ContainSingle(r =>
                r.OrderId == orderId && r.Status == ReservationStatus.Pending);

            var hasRow = await HasOutboxRowAsync(provider, "StockReservedIntegrationEvent", cts.Token);
            hasRow.Should().BeTrue("transactional outbox row proves the consume → aggregate → outbox chain");
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReserveStockConsumer_InsufficientStock_WritesFailureOutboxRowAndLeavesQuantityUnchanged()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var provider = BuildProvider();
        await SeedStockItemAsync(provider, productId, initialQuantity: 2);

        var busControl = provider.GetRequiredService<IBusControl>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await busControl.StartAsync(cts.Token);
        try
        {
            await busControl.Publish(new ReserveStockIntegrationEvent
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = orderId,
                ProductId = productId,
                Quantity = 5,
            }, cts.Token);

            await WaitForOutboxRowAsync(provider, "StockReservationFailedIntegrationEvent", cts.Token);

            var stockItem = await LoadStockItemAsync(provider, productId, cts.Token);
            stockItem.AvailableQuantity.Should().Be(2, "insufficient stock must leave available quantity unchanged");
            stockItem.Reservations.Should().BeEmpty("no reservation is created when stock is insufficient");
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReserveStockConsumer_SameEventDeliveredTwice_IdempotentSingleReservationAndSingleOutboxRow()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await using var provider = BuildProvider();
        await SeedStockItemAsync(provider, productId, initialQuantity: 10);

        var busControl = provider.GetRequiredService<IBusControl>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await busControl.StartAsync(cts.Token);
        try
        {
            var @event = new ReserveStockIntegrationEvent
            {
                Id = eventId,
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = orderId,
                ProductId = productId,
                Quantity = 3,
            };

            // First delivery: wait until reservation appears in DB.
            await busControl.Publish(@event, cts.Token);
            await WaitForPendingReservationAsync(provider, productId, orderId, cts.Token);

            // Second delivery of identical message: aggregate Reserve is idempotent (existing orderId → no-op).
            await busControl.Publish(@event, cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(4), cts.Token);

            var stockItem = await LoadStockItemAsync(provider, productId, cts.Token);
            stockItem.AvailableQuantity.Should().Be(7, "second consume is aggregate no-op; quantity unchanged");
            stockItem.Reservations.Should().ContainSingle("aggregate guard ensures exactly one reservation");

            // StockReserved is only raised on the first (real) Reserve call; no domain event → no new outbox row.
            var rowCount = await CountOutboxRowsByOrderIdAsync(
                provider, "StockReservedIntegrationEvent", orderId, cts.Token);
            rowCount.Should().Be(1, "aggregate-level idempotency prevents duplicate outbox writes");
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config);

        // Replace the prod localhost MassTransit registration with the test-container bus.
        RemoveMassTransitRegistrations(services);
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<ReserveStockConsumer>();
            bus.AddConsumer<ConfirmStockReservationConsumer>();
            bus.AddConsumer<ReleaseStockConsumer>();
            bus.UsingRabbitMq((context, rabbitMq) =>
            {
                rabbitMq.Host(new Uri(rabbit.ConnectionString));
                rabbitMq.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private static void RemoveMassTransitRegistrations(ServiceCollection services)
    {
        var toRemove = services
            .Where(d =>
                IsMassTransitType(d.ServiceType) ||
                IsMassTransitType(d.ImplementationType) ||
                IsMassTransitType(d.ImplementationInstance?.GetType()))
            .ToList();
        foreach (var descriptor in toRemove)
            services.Remove(descriptor);
    }

    private static bool IsMassTransitType(Type? type) =>
        type?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;

    private static async Task SeedStockItemAsync(
        IServiceProvider provider, Guid productId, int initialQuantity)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        ctx.StockItems.Add(StockItem.Create(productId, initialQuantity));
        await ctx.SaveChangesAsync();
    }

    private static async Task WaitForPendingReservationAsync(
        IServiceProvider provider, Guid productId, Guid orderId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using (var scope = provider.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var item = await ctx.StockItems
                    .Include(s => s.Reservations)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ProductId == productId, ct);
                if (item?.Reservations.Any(r => r.OrderId == orderId && r.Status == ReservationStatus.Pending) == true)
                    return;
            }

            await Task.Delay(250, ct);
        }
    }

    private static async Task WaitForOutboxRowAsync(
        IServiceProvider provider, string typeFragment, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using (var scope = provider.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                if (await ctx.OutboxMessages.AsNoTracking()
                    .AnyAsync(m => m.Type.Contains(typeFragment), ct))
                    return;
            }

            await Task.Delay(250, ct);
        }
    }

    private static async Task<StockItem> LoadStockItemAsync(
        IServiceProvider provider, Guid productId, CancellationToken ct)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await ctx.StockItems
            .Include(s => s.Reservations)
            .AsNoTracking()
            .FirstAsync(s => s.ProductId == productId, ct);
    }

    private static async Task<bool> HasOutboxRowAsync(
        IServiceProvider provider, string typeFragment, CancellationToken ct)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await ctx.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type.Contains(typeFragment), ct);
    }

    private static async Task<int> CountOutboxRowsByOrderIdAsync(
        IServiceProvider provider, string typeFragment, Guid orderId, CancellationToken ct)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await ctx.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type.Contains(typeFragment) && m.Payload.Contains(orderId.ToString()), ct);
    }
}
