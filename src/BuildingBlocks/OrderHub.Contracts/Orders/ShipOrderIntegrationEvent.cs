using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// Saga → OrderService <b>komutu</b> (ADR-0007 Karar 4): "şu siparişi kargolanmış işaretle" (Paid → Shipped). Saga,
/// tüm kalemlerin rezervasyonu onaylanınca (<c>StockReservationConfirmed</c> fan-out tamamlanınca) gönderir;
/// OrderService consumer'ı (5d-5) <c>ShipOrderCommand</c>'e map'leyip <c>ISender</c> ile tetikler → saga
/// <c>Completed</c>'a geçer. Tek mantıksal tüketici → command-style, RabbitMQ (<see cref="IRabbitMqEvent"/>).
/// <see cref="Id"/> uçtan uca dedup anahtarıdır (ADR-0002 Karar 4).
/// </summary>
public sealed record ShipOrderIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Kargolanmış işaretlenecek siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }
}
