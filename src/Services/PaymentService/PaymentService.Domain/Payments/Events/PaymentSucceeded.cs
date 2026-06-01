using OrderHub.Common.Primitives;

namespace OrderHub.PaymentService.Domain.Payments.Events;

/// <summary>
/// Bir ödeme başarıyla tamamlandığında yükseltilen domain olayı. Faz 3c'de outbox üzerinden
/// <c>PaymentSucceededIntegrationEvent</c>'e köprülenir (OrderService siparişi <c>Paid</c>'e geçirir);
/// <paramref name="OrderId"/> ve <paramref name="ExternalTransactionId"/> bu olayda taşınır.
/// <c>EventId</c> (DomainEvent tabanı) uçtan uca dedup anahtarıdır.
/// </summary>
/// <param name="PaymentId">Ödeme kimliği.</param>
/// <param name="OrderId">Ödemenin ait olduğu sipariş kimliği.</param>
/// <param name="ExternalTransactionId">Sağlayıcının döndürdüğü dış işlem kimliği.</param>
public sealed record PaymentSucceeded(Guid PaymentId, Guid OrderId, string ExternalTransactionId) : DomainEvent;
