namespace OrderHub.Contracts.Orders;

/// <summary>
/// Bir sipariş onaylandı (Kafka event-stream, ADR-0006). AnalyticsService projection'ı status'u Confirmed'e
/// günceller. Kaynak: <c>OrderConfirmed</c> domain olayı — bu olay <b>1:N fan-out</b> edilir: RabbitMQ'ya
/// <c>ProcessPaymentIntegrationEvent</c> (command) + Kafka'ya bu event (notification). İkisi de aynı EventId'yi
/// taşır (composite outbox PK (Id, Ordinal) ayırır, ADR-0006 Karar 4). Tutar primitive — <c>Money</c> sızdırılmaz.
/// </summary>
public sealed record OrderConfirmedIntegrationEvent : OrderStreamEvent
{
    /// <summary>Sipariş sahibinin müşteri kimliği.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Sipariş toplam tutarı.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Para birimi (ISO 4217 kodu, ör. "TRY").</summary>
    public required string Currency { get; init; }
}
