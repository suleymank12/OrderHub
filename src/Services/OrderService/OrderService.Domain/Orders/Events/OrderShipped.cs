using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>
/// Bir sipariş kargolandığında (Paid → Shipped) yükseltilen domain olayı. Faz 5'te yalnızca <b>in-process</b>
/// kalır (outbox registry'sinde karşılığı yok; saga akışını <c>ShipOrder</c> command'i sürükler);
/// ileride Kafka event-stream'ine köprülenebilir (<see cref="OrderPaid"/> precedent'iyle aynı).
/// </summary>
/// <param name="OrderId">Kargolanan siparişin kimliği.</param>
public sealed record OrderShipped(Guid OrderId) : DomainEvent;
