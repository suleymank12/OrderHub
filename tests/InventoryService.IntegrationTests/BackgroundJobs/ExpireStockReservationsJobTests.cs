using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.InventoryService.Application;
using OrderHub.InventoryService.Application.Stock.BackgroundJobs;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Infrastructure;
using OrderHub.InventoryService.IntegrationTests.Fixtures;

namespace OrderHub.InventoryService.IntegrationTests.BackgroundJobs;

/// <summary>
/// Faz 5 5c — ExpireStockReservationsJob integration testi: broker gerekmez, yalnız SQL container.
/// Seeding fixture.CreateContext() ile yapılır (outbox interceptor yok → seeding satırı yazılmaz);
/// job ise AddInfrastructure DI aracılığıyla çalışır (interceptor var → expiry outbox satırı yazılır).
/// BuildServiceProvider hosted service başlatmaz → RecurringJobRegistrar/OutboxProcessor dormant.
/// </summary>
public sealed class ExpireStockReservationsJobTests(InventorySqlServerContainerFixture fixture)
    : IClassFixture<InventorySqlServerContainerFixture>
{
    [Fact]
    public async Task ExecuteAsync_PendingReservationPastExpiry_ExpiresAndRestoresQuantityAndWritesOutboxRow()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        const int initialQuantity = 10;

        await SeedExpiredReservationAsync(productId, orderId, reservedQuantity: 3);

        await using var provider = BuildProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpireStockReservationsJob>();
            await job.ExecuteAsync(CancellationToken.None);
        }

        await using var ctx = fixture.CreateContext();
        var stockItem = await ctx.StockItems
            .Include(s => s.Reservations)
            .AsNoTracking()
            .FirstAsync(s => s.ProductId == productId);

        stockItem.Reservations.Should().ContainSingle(r =>
            r.OrderId == orderId && r.Status == ReservationStatus.Expired);
        stockItem.AvailableQuantity.Should().Be(initialQuantity,
            "expiry returns the reserved quantity (3) to the available pool");

        // Order-scoped (payload = orderId): paylaşılan DB'de kardeş testin satırında yanlış-pozitif olmasın
        // (senkron job → flaky değil; scope tutarlılık + doğruluk için, sibling satır 89 deseniyle aynı).
        var hasRow = await ctx.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type.Contains("StockReservationExpiredIntegrationEvent") && m.Payload.Contains(orderId.ToString()));
        hasRow.Should().BeTrue("job commits expiry event atomically via transactional outbox");
    }

    [Fact]
    public async Task ExecuteAsync_RunTwice_IdempotentNoDuplicateExpiryOrQuantityRestore()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        const int initialQuantity = 10;

        await SeedExpiredReservationAsync(productId, orderId, reservedQuantity: 3);

        await using var provider = BuildProvider();

        // First run: expires the pending reservation.
        await using (var scope = provider.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpireStockReservationsJob>();
            await job.ExecuteAsync(CancellationToken.None);
        }

        // Second run: no pending expired reservations → early return, no new side effects.
        await using (var scope = provider.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpireStockReservationsJob>();
            await job.ExecuteAsync(CancellationToken.None);
        }

        await using var ctx = fixture.CreateContext();
        var stockItem = await ctx.StockItems
            .Include(s => s.Reservations)
            .AsNoTracking()
            .FirstAsync(s => s.ProductId == productId);

        stockItem.AvailableQuantity.Should().Be(initialQuantity, "no double-restore on second run");
        stockItem.Reservations.Should().ContainSingle(r => r.Status == ReservationStatus.Expired);

        var expiredRowCount = await ctx.OutboxMessages.AsNoTracking()
            .CountAsync(m =>
                m.Type.Contains("StockReservationExpiredIntegrationEvent") &&
                m.Payload.Contains(orderId.ToString()));
        expiredRowCount.Should().Be(1, "idempotency ensures exactly one outbox row per reservation expiry");
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config);
        return services.BuildServiceProvider();
    }

    private async Task SeedExpiredReservationAsync(Guid productId, Guid orderId, int reservedQuantity)
    {
        await using var ctx = fixture.CreateContext();
        var stockItem = StockItem.Create(productId, 10);
        // ExpiresAtUtc in the past so the job's GetWithExpiredReservationsAsync picks it up.
        stockItem.Reserve(orderId, reservedQuantity, DateTime.UtcNow.AddMinutes(-1));
        ctx.StockItems.Add(stockItem);
        await ctx.SaveChangesAsync();
    }
}
