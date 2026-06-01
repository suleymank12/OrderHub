using OrderHub.Common.Primitives;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>Yeni bir sipariş oluşturulduğunda yükseltilen domain olayı.</summary>
/// <param name="OrderId">Siparişin kimliği.</param>
/// <param name="CustomerId">Müşteri kimliği.</param>
/// <param name="Total">Sipariş toplam tutarı.</param>
public sealed record OrderCreated(Guid OrderId, Guid CustomerId, Money Total) : DomainEvent;
