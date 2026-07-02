using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// Saga → OrderService <b>komutu</b> (ADR-0007 Karar 4): "şu siparişi onayla" (Pending → Confirmed). Saga,
/// tüm kalemlerin stoğu ayrılınca (<c>StockReserved</c> fan-out tamamlanınca) bunu <c>ProcessPayment</c> ile
/// birlikte gönderir; OrderService consumer'ı (5d-5) <c>ConfirmOrderCommand</c>'e map'leyip <c>ISender</c> ile
/// tetikler. Tek mantıksal tüketici (OrderService) → command-style, RabbitMQ (<see cref="IRabbitMqEvent"/>).
/// <see cref="Id"/> uçtan uca dedup anahtarıdır (ADR-0002 Karar 4).
/// </summary>
public sealed record ConfirmOrderIntegrationEvent : IRabbitMqEvent
{
    /// <summary>Olay kimliği (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Onaylanacak siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }
}
