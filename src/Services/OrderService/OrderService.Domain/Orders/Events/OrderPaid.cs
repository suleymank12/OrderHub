using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>
/// Bir sipariş ödendiğinde (Confirmed → Paid) yükseltilen domain olayı. Faz 3'te yalnızca <b>in-process</b>
/// kalır (outbox registry'sinde karşılığı yok); ileride Kafka event-stream'ine (Faz 4 analytics) köprülenebilir.
/// </summary>
/// <param name="OrderId">Ödenen siparişin kimliği.</param>
public sealed record OrderPaid(Guid OrderId) : DomainEvent;
