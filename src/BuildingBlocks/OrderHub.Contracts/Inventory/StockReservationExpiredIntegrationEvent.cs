using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// InventoryService → Saga zaman-aşımı olayı (ADR-0007 Karar 2/4): "şu siparişin stok rezervasyonu 15dk içinde
/// onaylanmadı, zaman aşımıyla iade edildi". Hangfire expiry job'undan <c>StockItem.ExpireReservation</c> sonrası
/// outbox ile yayımlanır; saga (5d) bunu compensation sinyali olarak tüketir (expiry ≠ release — ayrı sinyal). RabbitMQ.
/// </summary>
public sealed record StockReservationExpiredIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak <c>StockReservationExpired.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu zaman aşımına uğrayan sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>İade edilen miktar.</summary>
    public required int Quantity { get; init; }
}
