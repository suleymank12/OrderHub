using OrderHub.Common.Primitives;

namespace OrderHub.PaymentService.Domain.Payments.Events;

/// <summary>
/// Bir ödeme başarısız olduğunda yükseltilen domain olayı. Faz 3c'de outbox üzerinden
/// <c>PaymentFailedIntegrationEvent</c>'e köprülenir (OrderService siparişi <c>Cancelled</c>'a geçirir);
/// <paramref name="OrderId"/> ve <paramref name="Reason"/> taşınır. <c>EventId</c> dedup anahtarıdır.
/// </summary>
/// <param name="PaymentId">Ödeme kimliği.</param>
/// <param name="OrderId">Ödemenin ait olduğu sipariş kimliği.</param>
/// <param name="Reason">Başarısızlık gerekçesi.</param>
public sealed record PaymentFailed(Guid PaymentId, Guid OrderId, string Reason) : DomainEvent;
