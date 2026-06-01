using OrderHub.Common.Primitives;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>
/// Bir sipariş onaylandığında yükseltilen domain olayı. Faz 3'te outbox üzerinden
/// <c>ProcessPaymentIntegrationEvent</c>'e köprülenir; ödeme tutarı (<paramref name="Total"/>) ve müşteri
/// (<paramref name="CustomerId"/>) bu olayda taşınır ki çeviri saf kalsın (ekstra DB okuması olmadan,
/// <see cref="OrderCreated"/> precedent'iyle aynı).
/// </summary>
/// <param name="OrderId">Onaylanan siparişin kimliği.</param>
/// <param name="CustomerId">Müşteri kimliği.</param>
/// <param name="Total">Sipariş toplam tutarı (ödeme bu tutar üzerinden işlenir).</param>
public sealed record OrderConfirmed(Guid OrderId, Guid CustomerId, Money Total) : DomainEvent;
