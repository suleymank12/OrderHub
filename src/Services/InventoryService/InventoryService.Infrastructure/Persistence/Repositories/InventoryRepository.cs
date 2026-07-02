using Microsoft.EntityFrameworkCore;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Domain.Stock;

namespace OrderHub.InventoryService.Infrastructure.Persistence.Repositories;

/// <summary>
/// IInventoryRepository'nin EF Core implementasyonu. Yalnızca persistence; iş kuralı içermez.
/// IQueryable dışarı sızdırmaz — tüm sorgular burada materialize edilir.
/// </summary>
internal sealed class InventoryRepository(InventoryDbContext context) : IInventoryRepository
{
    /// <summary>
    /// Ürün kimliğiyle stok kalemini tracked + eager yükler (yazma yolu).
    /// AsNoTracking yoktur — TransactionBehavior ile aynı scope'ta SaveChanges çağrılır.
    /// </summary>
    public async Task<StockItem?> GetByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken) =>
        await context.StockItems
            .Include(stockItem => stockItem.Reservations)
            .FirstOrDefaultAsync(stockItem => stockItem.ProductId == productId, cancellationToken);

    /// <summary>
    /// En az bir Pending rezervasyonu süresi dolmuş stok kalemlerini tracked + eager yükler.
    /// ExpireStockReservationsJob bu sonuçları mutate ederek SaveChanges ile atomik yazar.
    /// </summary>
    public async Task<IReadOnlyList<StockItem>> GetWithExpiredReservationsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var items = await context.StockItems
            .Include(stockItem => stockItem.Reservations)
            .Where(stockItem => stockItem.Reservations.Any(
                reservation => reservation.Status == ReservationStatus.Pending
                            && reservation.ExpiresAtUtc < nowUtc))
            .ToListAsync(cancellationToken);

        return items;
    }
}
