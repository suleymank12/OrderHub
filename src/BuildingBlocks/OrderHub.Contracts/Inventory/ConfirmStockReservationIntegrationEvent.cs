using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// Saga → InventoryService komutu (ADR-0007 Karar 4): "şu siparişin stok rezervasyonunu onayla" (ödeme
/// başarılı → stok kalıcı düşer). InventoryService consumer'ı → <c>StockItem.ConfirmReservation</c>. Onay
/// sonucu için ayrı bir result event yoktur (saga confirm sonrası Completed'a gider). Command-style → RabbitMQ.
/// </summary>
public sealed record ConfirmStockReservationIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu onaylanacak sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği (stok kalemini bulmak için).</summary>
    public required Guid ProductId { get; init; }
}
