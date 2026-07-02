using OrderHub.EventBus;

namespace OrderHub.Contracts.Inventory;

/// <summary>
/// Saga → InventoryService telafi komutu (ADR-0007 Karar 4): "şu siparişin stok rezervasyonunu serbest bırak"
/// (ödeme başarısız → compensation). InventoryService consumer'ı → <c>StockItem.ReleaseReservation</c>; sonuç
/// <see cref="StockReleasedIntegrationEvent"/>. Command-style → RabbitMQ.
/// </summary>
public sealed record ReleaseStockIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Rezervasyonu serbest bırakılacak sipariş kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İlgili ürün kimliği (stok kalemini bulmak için).</summary>
    public required Guid ProductId { get; init; }
}
