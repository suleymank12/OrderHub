using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// InventoryService → Saga telafi-onay olayı (ADR-0007 Karar 4): "şu siparişin stok rezervasyonu serbest
/// bırakıldı" (compensation ack). <c>StockItem.ReleaseReservation</c> sonrası outbox ile yayımlanır; saga (5d)
/// bunu tüketip CancelOrder'a geçer. <see cref="Id"/> kaynak <c>StockReleased</c> domain olayının EventId'sidir. RabbitMQ.
/// </summary>
public sealed record StockReleasedIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak <c>StockReleased.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu serbest bırakılan sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>İade edilen miktar.</summary>
    public required int Quantity { get; init; }
}
