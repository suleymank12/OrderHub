using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// InventoryService → Saga sonuç olayı (ADR-0007 Karar 4): "şu sipariş için stok başarıyla ayrıldı".
/// <c>StockItem.Reserve</c> sonrası outbox ile yayımlanır; saga (5d) bunu tüketip ödeme adımına geçer.
/// <see cref="Id"/> kaynak <c>StockReserved</c> domain olayının EventId'sidir (uçtan uca dedup). RabbitMQ.
/// </summary>
public sealed record StockReservedIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak <c>StockReserved.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Stok ayrılan sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>Ayrılan miktar.</summary>
    public required int Quantity { get; init; }
}
