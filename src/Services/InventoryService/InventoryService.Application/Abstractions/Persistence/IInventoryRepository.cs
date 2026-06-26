using OrderHub.InventoryService.Domain.Stock;

namespace OrderHub.InventoryService.Application.Abstractions.Persistence;

/// <summary>
/// <see cref="StockItem"/> aggregate'i için persistence portu. Application'da tanımlı, Infrastructure EF Core
/// ile implement eder (DIP). Yalnızca persistence; iş kuralı içermez ve <c>IQueryable</c> sızdırmaz (§9).
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Ürün kimliğiyle stok kalemini yükler; bulunamazsa <c>null</c> döner.
    /// Yazma yolu için: aggregate ve <c>Reservations</c> koleksiyonu <b>tracked + eager loaded</b> olarak
    /// döner. Davranış mutasyona açık (<c>AsNoTracking</c> yoktur) — TransactionBehavior ile
    /// aynı DbContext scope'unda SaveChanges çağrılır.
    /// </summary>
    Task<StockItem?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="nowUtc"/> anında en az bir <c>Pending</c> rezervasyonu süresi dolmuş
    /// stok kalemlerini döner (expiry job'u için). Her kalem <c>Reservations</c> ile birlikte
    /// <b>tracked + eager loaded</b> gelir — <c>ExpireReservation</c> çağrılarının SaveChanges'te
    /// atomik yazılabilmesi için gereklidir.
    /// </summary>
    Task<IReadOnlyList<StockItem>> GetWithExpiredReservationsAsync(
        DateTime nowUtc, CancellationToken cancellationToken);
}
