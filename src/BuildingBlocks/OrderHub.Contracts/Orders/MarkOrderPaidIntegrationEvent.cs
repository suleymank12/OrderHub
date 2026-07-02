using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// Saga → OrderService <b>komutu</b> (ADR-0007 Karar 4): "şu siparişi ödenmiş işaretle" (Confirmed → Paid). Saga,
/// <c>PaymentSucceeded</c> alınca gönderir; OrderService consumer'ı (5d-5) <c>MarkOrderPaidCommand</c>'e map'leyip
/// <c>ISender</c> ile tetikler. <c>MarkPaid</c> yalnız Confirmed siparişte geçerli olduğundan — ConfirmOrder ile
/// arada yarış olursa — consumer, sipariş henüz Confirmed değilse retry'a güvenir (5d-4 Karar D). Tek tüketici →
/// command-style, RabbitMQ (<see cref="IRabbitMqEvent"/>). <see cref="Id"/> uçtan uca dedup anahtarı.
/// </summary>
public sealed record MarkOrderPaidIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Ödenmiş işaretlenecek siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }
}
