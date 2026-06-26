namespace OrderHub.Contracts.Orders;

/// <summary>
/// Bir sipariş oluşturuldu (Kafka event-stream, ADR-0006). AnalyticsService projection'ı yeni siparişi
/// kaydeder. Kaynak: <c>OrderCreated</c> domain olayı (Faz 3'te yalnız in-process'ti; Faz 4'te ek olarak
/// Kafka'ya). Tutar primitive (<see cref="Amount"/> + <see cref="Currency"/>) — <c>Money</c> sızdırılmaz.
/// </summary>
public sealed record OrderCreatedIntegrationEvent : OrderStreamEvent
{
    /// <summary>Sipariş sahibinin müşteri kimliği.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Sipariş toplam tutarı.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Para birimi (ISO 4217 kodu, ör. "TRY").</summary>
    public required string Currency { get; init; }
}
