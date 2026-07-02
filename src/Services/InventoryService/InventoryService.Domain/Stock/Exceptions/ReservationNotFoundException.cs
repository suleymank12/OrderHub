using OrderHub.Common.Exceptions;

namespace OrderHub.InventoryService.Domain.Stock.Exceptions;

/// <summary>
/// Bir sipariş için confirm/release istenirken o siparişe ait rezervasyon bulunamadığında fırlatılır.
/// Normal saga akışında olmaz (confirm/release her zaman bir reserve'den sonra gelir) → anomali sinyali.
/// </summary>
public sealed class ReservationNotFoundException : DomainException
{
    /// <summary>İlgili stok kalemi kimliği.</summary>
    public Guid StockItemId { get; }

    /// <summary>Rezervasyonu aranan sipariş kimliği.</summary>
    public Guid OrderId { get; }

    /// <summary>Stok kalemi ve sipariş kimliğiyle exception oluşturur.</summary>
    public ReservationNotFoundException(Guid stockItemId, Guid orderId)
        : base($"No reservation found for order {orderId} on stock item {stockItemId}.")
    {
        StockItemId = stockItemId;
        OrderId = orderId;
    }
}
