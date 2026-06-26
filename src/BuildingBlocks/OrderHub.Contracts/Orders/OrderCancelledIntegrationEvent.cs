namespace OrderHub.Contracts.Orders;

/// <summary>
/// Bir sipariş iptal edildi (Kafka event-stream, ADR-0006). AnalyticsService projection'ı status'u Cancelled'e
/// günceller. Kaynak: <c>OrderCancelled</c> domain olayı (Faz 3'te yalnız in-process'ti). <see cref="Reason"/>
/// iptal gerekçesini taşır (audit/analytics).
/// </summary>
public sealed record OrderCancelledIntegrationEvent : OrderStreamEvent
{
    /// <summary>İptal gerekçesi.</summary>
    public required string Reason { get; init; }
}
