using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>Bir sipariş iptal edildiğinde yükseltilen domain olayı.</summary>
/// <param name="OrderId">İptal edilen siparişin kimliği.</param>
/// <param name="Reason">İptal gerekçesi.</param>
public sealed record OrderCancelled(Guid OrderId, string Reason) : DomainEvent;
