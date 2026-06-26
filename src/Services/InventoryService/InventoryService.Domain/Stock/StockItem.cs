using OrderHub.Common.Primitives;
using OrderHub.InventoryService.Domain.Stock.Events;
using OrderHub.InventoryService.Domain.Stock.Exceptions;

namespace OrderHub.InventoryService.Domain.Stock;

/// <summary>
/// Bir ürünün stok kalemi aggregate root'u. <b>Tutarlılık sınırı:</b> "ayrılan miktar ≤ mevcut miktar"
/// invariant'ı <see cref="Reservation"/>'larla birlikte TEK aggregate içinde korunur (transactional
/// consistency) — rezervasyonlar bu kökten yönetilir, dışarıdan değil. Eşzamanlı reserve/confirm/release
/// optimistic concurrency (shadow <c>RowVersion</c>, EF config) + retry ile serileşir (ADR-0005/0007 felsefesi).
/// <see cref="Catalog.Product"/> AYRI hafif aggregate'tir (katalog) — stok burada, katalog orada. Hiçbir setter public değil.
/// </summary>
public sealed class StockItem : AggregateRoot<Guid>
{
    private readonly List<Reservation> _reservations = [];

    private StockItem()
    {
    }

    /// <summary>Bu stok kaleminin ait olduğu ürün kimliği (<see cref="Catalog.Product"/> ref).</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Şu an satılabilir (ayrılmamış) miktar. Reserve düşürür, release/expiry iade eder; asla &lt; 0.</summary>
    public int AvailableQuantity { get; private set; }

    /// <summary>Bu kalem üzerindeki rezervasyonlar (salt-okunur; yalnız behavior'lar ekler/değiştirir).</summary>
    public IReadOnlyList<Reservation> Reservations => _reservations.AsReadOnly();

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Yeni stok kalemi oluşturur. <paramref name="productId"/> boş → <see cref="ArgumentException"/>;
    /// <paramref name="initialQuantity"/> negatif → <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static StockItem Create(Guid productId, int initialQuantity)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id cannot be empty.", nameof(productId));
        }

        if (initialQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialQuantity), initialQuantity, "Initial quantity cannot be negative.");
        }

        return new StockItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AvailableQuantity = initialQuantity,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Bir sipariş için <paramref name="quantity"/> kadar stok ayırır (Pending rezervasyon) ve
    /// <see cref="StockReserved"/> yükseltir. <b>Idempotent:</b> aynı sipariş için zaten aktif rezervasyon
    /// varsa no-op (saga event re-delivery koruması). Invariant: <paramref name="quantity"/> &gt;
    /// <see cref="AvailableQuantity"/> → <see cref="InsufficientStockException"/>.
    /// </summary>
    public void Reserve(Guid orderId, int quantity, DateTime expiresAtUtc)
    {
        EnsureOrderId(orderId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Reservation quantity must be positive.");
        }

        if (FindReservation(orderId) is not null)
        {
            return; // idempotent: aynı OrderId için ReserveStock komutu ikinci kez geldi → tekrar ayırma.
        }

        if (quantity > AvailableQuantity)
        {
            throw new InsufficientStockException(Id, ProductId, quantity, AvailableQuantity);
        }

        AvailableQuantity -= quantity;
        _reservations.Add(Reservation.Create(orderId, quantity, expiresAtUtc));
        RaiseDomainEvent(new StockReserved(Id, ProductId, orderId, quantity));
    }

    /// <summary>
    /// Sipariş rezervasyonunu onaylar (Pending → Confirmed); stok kalıcı düşer (available değişmez —
    /// reserve'de zaten düşürüldü) ve <see cref="StockReservationConfirmed"/> yükseltir. Idempotent: zaten
    /// Confirmed ise no-op. Rezervasyon yoksa → <see cref="ReservationNotFoundException"/>.
    /// </summary>
    public void ConfirmReservation(Guid orderId)
    {
        var reservation = RequireReservation(orderId);
        if (reservation.Confirm()) // yalnız GERÇEK geçişte event (zaten Confirmed → no-op, duplicate event yok).
        {
            RaiseDomainEvent(new StockReservationConfirmed(Id, orderId));
        }
    }

    /// <summary>
    /// Sipariş rezervasyonunu serbest bırakır (Pending → Released, compensation); ayrılan miktar
    /// <see cref="AvailableQuantity"/>'ye iade edilir ve <see cref="StockReleased"/> yükseltilir. Idempotent:
    /// zaten Released ise no-op (stok iki kez iade edilmez). Rezervasyon yoksa →
    /// <see cref="ReservationNotFoundException"/>; Confirmed'ı release → <see cref="InvalidReservationStatusTransitionException"/>.
    /// </summary>
    public void ReleaseReservation(Guid orderId)
    {
        var reservation = RequireReservation(orderId);
        if (reservation.Release()) // zaten Released → false (iade tekrarı yok); Confirmed → InvalidTransition.
        {
            AvailableQuantity += reservation.Quantity; // stok yalnız gerçek iadede geri eklenir.
            RaiseDomainEvent(new StockReleased(Id, orderId, reservation.Quantity));
        }
    }

    private Reservation RequireReservation(Guid orderId)
    {
        EnsureOrderId(orderId);
        return FindReservation(orderId) ?? throw new ReservationNotFoundException(Id, orderId);
    }

    private Reservation? FindReservation(Guid orderId) =>
        _reservations.Find(reservation => reservation.OrderId == orderId);

    private static void EnsureOrderId(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id cannot be empty.", nameof(orderId));
        }
    }
}
