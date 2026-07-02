using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// Saga → OrderService <b>komutu</b> (ADR-0007 Karar 4/5, compensation): "şu siparişi iptal et". Saga, telafi
/// dalında (stok ayrılamadı → StockReservationFailed, ya da ödeme reddedildi → PaymentFailed) rezervasyonlar
/// serbest bırakıldıktan sonra gönderir; OrderService consumer'ı (5e-1) <c>CancelOrderCommand</c>'e map'leyip
/// <c>ISender</c> ile tetikler → <c>Order.Cancel(Reason)</c>. Tek mantıksal tüketici (OrderService) →
/// command-style, RabbitMQ (<see cref="IRabbitMqEvent"/>). Saga bunu 5e-2'de gönderir (bu adımda consumer dormant).
/// <see cref="Id"/> uçtan uca dedup anahtarıdır (ADR-0002 Karar 4).
/// </summary>
public sealed record CancelOrderIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>İptal edilecek siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>İptal gerekçesi (hangi telafi dalı: <c>stock_unavailable</c> / <c>payment_failed</c>) — audit/analytics.</summary>
    public required string Reason { get; init; }
}
