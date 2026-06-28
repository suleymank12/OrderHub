using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// OrderService → Saga <b>tetik</b> olayı (ADR-0007 Karar 4): "şu sipariş yerleşti, saga'yı başlat". Sipariş
/// yerleşimi sonrası outbox üzerinden yayımlanır; saga (5d-4) bu tipi tüketip her kalem için bir
/// <see cref="Inventory.ReserveStockIntegrationEvent"/> kurar ve ödeme adımında <see cref="Amount"/> +
/// <see cref="Currency"/> ile <c>ProcessPayment</c> üretir. Saga <b>tek</b> mantıksal tüketicidir → command-style,
/// RabbitMQ (<see cref="IRabbitMqEvent"/>, ADR-0007 Karar 4).
/// <para>
/// <see cref="Id"/> kaynak domain olayının EventId'sini taşır (uçtan uca dedup, ADR-0002 Karar 4). Kalemler
/// <see cref="OrderPlacedItem"/> listesi olarak taşınır — saga'nın N adet <c>ReserveStock</c> kurabilmesi için
/// ürün + miktar bilgisi gerekir. Tutar bilinçli olarak primitive (<c>decimal</c> + ISO 4217 <c>string</c>)
/// taşınır → <c>Money</c> value object'i sızdırılmaz, saga OrderService domain'ine bağlanmaz.
/// </para>
/// </summary>
public sealed record OrderPlacedIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği = kaynak domain olayının EventId'si (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Saga'yı tetikleyen sipariş kimliği (saga correlation'ı).</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Sipariş kalemleri — saga her biri için bir <c>ReserveStock</c> komutu kurar.</summary>
    public required IReadOnlyList<OrderPlacedItem> Items { get; init; }

    /// <summary>Ödeme adımında çekilecek toplam tutar (negatif olamaz — kaynak <c>Money</c> bunu garanti eder).</summary>
    public required decimal Amount { get; init; }

    /// <summary>Para birimi (ISO 4217 kodu, ör. "TRY").</summary>
    public required string Currency { get; init; }
}

/// <summary>
/// <see cref="OrderPlacedIntegrationEvent"/> içindeki tek bir sipariş kalemi: saga'nın per-ürün
/// <c>ReserveStock</c> komutunu kurabilmesi için gereken minimum bilgi (ürün + miktar). Fiyat/ad gibi
/// alanlar bilinçli olarak taşınmaz — saga yalnızca stok ayırma için ürün kimliği ve miktara ihtiyaç duyar.
/// </summary>
/// <param name="ProductId">Stok ayrılacak ürün kimliği.</param>
/// <param name="Quantity">Ayrılacak miktar (pozitif).</param>
public sealed record OrderPlacedItem(Guid ProductId, int Quantity);
