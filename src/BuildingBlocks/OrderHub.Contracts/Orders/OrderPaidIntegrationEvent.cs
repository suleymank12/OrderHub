namespace OrderHub.Contracts.Orders;

/// <summary>
/// Bir sipariş ödendi (Kafka event-stream, ADR-0006). AnalyticsService projection'ı status'u Paid'e güncelleyip
/// ödeme zamanını (<see cref="OrderStreamEvent.OccurredOnUtc"/>) ve revenue aggregate'ini işler. Kaynak:
/// <c>OrderPaid</c> domain olayı (Faz 3'te yalnız in-process'ti). Ek alan yok — sipariş kimliği + zaman yeterli.
/// </summary>
public sealed record OrderPaidIntegrationEvent : OrderStreamEvent;
