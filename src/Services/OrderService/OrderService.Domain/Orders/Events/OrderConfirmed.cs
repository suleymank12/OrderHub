using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>Bir sipariş onaylandığında yükseltilen domain olayı.</summary>
/// <param name="OrderId">Onaylanan siparişin kimliği.</param>
public sealed record OrderConfirmed(Guid OrderId) : DomainEvent;
