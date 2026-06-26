using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// Saga → InventoryService komut-tarzı entegrasyon olayı (ADR-0007 Karar 4): "şu sipariş için şu üründen
/// <see cref="Quantity"/> adet ayır". Saga (5d) <c>OrderCreated</c> sonrası yayımlar; InventoryService consumer'ı
/// tüketir → <c>StockItem.Reserve</c>. Sonuç: <see cref="StockReservedIntegrationEvent"/> veya
/// <see cref="StockReservationFailedIntegrationEvent"/>. Command-style → RabbitMQ (<see cref="IRabbitMqEvent"/>).
/// </summary>
public sealed record ReserveStockIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu tetikleyen sipariş kimliği (saga correlation'ı).</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Stok ayrılacak ürün kimliği.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>Ayrılacak miktar.</summary>
    public required int Quantity { get; init; }
}
