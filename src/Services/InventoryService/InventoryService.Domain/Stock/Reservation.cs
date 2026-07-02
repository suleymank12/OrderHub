using OrderHub.Common.Primitives;
using OrderHub.InventoryService.Domain.Stock.Exceptions;

namespace OrderHub.InventoryService.Domain.Stock;

/// <summary>
/// Bir sipariş için <see cref="StockItem"/> üzerinde ayrılmış stok miktarı. <b>Aggregate root DEĞİLDİR</b> —
/// <see cref="StockItem"/> aggregate'inin bir parçasıdır (entity). Durum geçişleri yalnızca sahip aggregate'in
/// (StockItem) behavior'larından <c>internal</c> tetiklenir; doğrudan dışarıdan mutate edilemez ("reserved ≤
/// available" invariant'ı tek sınırda — StockItem'da — korunur). Hiçbir setter public değil.
/// </summary>
public sealed class Reservation : Entity<Guid>
{
    private Reservation()
    {
    }

    /// <summary>Bu rezervasyonu yapan sipariş kimliği (saga correlation'ı = OrderId).</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Ayrılan miktar (pozitif).</summary>
    public int Quantity { get; private set; }

    /// <summary>Mevcut durum.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Geçerlilik bitişi (UTC); bu andan sonra 15dk-timeout job'u serbest bırakır (5c).</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>StockItem.Reserve içinden çağrılır — yeni Pending rezervasyon (kimlik üretilir).</summary>
    internal static Reservation Create(Guid orderId, int quantity, DateTime expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Quantity = quantity,
        Status = ReservationStatus.Pending,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = expiresAtUtc
    };

    /// <summary>
    /// Pending → Confirmed. Durum <b>gerçekten değiştiyse</b> <c>true</c>, zaten Confirmed ise (idempotent no-op)
    /// <c>false</c> döner; Pending-dışı başka bir terminal'den çağrılırsa geçersiz geçiş. Dönüş, sahip aggregate'in
    /// no-op'ta domain event RAISE ETMEMESİ için kullanılır (Order.MarkPaid no-op precedent'i).
    /// </summary>
    internal bool Confirm() => Transition(ReservationStatus.Confirmed);

    /// <summary>Pending → Released (compensation). Değiştiyse <c>true</c>, zaten Released ise <c>false</c>.</summary>
    internal bool Release() => Transition(ReservationStatus.Released);

    /// <summary>Pending → Expired (15dk timeout). Değiştiyse <c>true</c>, zaten Expired ise <c>false</c>.</summary>
    internal bool Expire() => Transition(ReservationStatus.Expired);

    // Pending'den tek-yönlü terminal geçiş. Aynı hedefe tekrar = idempotent no-op → false (event bastırılır).
    // Pending dışı bir kaynaktan farklı bir hedefe geçiş = anlamsız (ör. Confirmed'ı Release) → exception.
    private bool Transition(ReservationStatus target)
    {
        if (Status == target)
        {
            return false; // idempotent: aynı saga komutu ikinci kez geldi → değişim yok.
        }

        if (Status != ReservationStatus.Pending)
        {
            throw new InvalidReservationStatusTransitionException(Status, target);
        }

        Status = target;
        return true;
    }
}
