using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// InventoryService → Saga sonuç olayı (ADR-0007 Karar 4): "şu sipariş için stok ayrılamadı (yetersiz stok)".
/// Stok-yetersiz NORMAL iş sonucudur (PaymentFailed precedent'i) → saga compensation'a girer (order cancel).
/// <c>StockReservationFailed</c> domain olayından outbox ile köprülenir. RabbitMQ.
/// </summary>
public sealed record StockReservationFailedIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak <c>StockReservationFailed.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Stok ayrılamayan sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>Başarısızlık gerekçesi (ör. yetersiz stok detayı).</summary>
    public required string Reason { get; init; }
}
