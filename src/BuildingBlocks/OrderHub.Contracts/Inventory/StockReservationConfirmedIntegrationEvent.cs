using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// InventoryService → Saga <b>sonuç</b> olayı (ADR-0007 Karar 4): "şu siparişin şu ürünü için stok rezervasyonu
/// onaylandı" (ödeme başarılı → stok kalıcı düştü). Kaynak: <c>StockReservationConfirmed</c> domain olayı
/// (<c>StockItem.ConfirmReservation</c> sonrası); 5d-3'te outbox üzerinden saga'ya köprülenir. Saga (5d-4)
/// per-ürün confirm sayacında <see cref="ProductId"/> ile ilerler (tüm kalemler onaylanınca <c>Completed</c>).
/// <see cref="Id"/> kaynak <c>StockReservationConfirmed.EventId</c>'sidir (uçtan uca dedup). Command-style → RabbitMQ.
/// </summary>
public sealed record StockReservationConfirmedIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak <c>StockReservationConfirmed.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu onaylanan sipariş kimliği (saga correlation'ı).</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Onaylanan ürün kimliği — saga'nın per-ürün confirm sayacı için.</summary>
    public required Guid ProductId { get; init; }
}
