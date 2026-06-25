using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.FailOrderPayment;

/// <summary>
/// Bir siparişi ödeme başarısızlığı nedeniyle iptal eder (PaymentFailed akışı). 3c-3'te
/// <c>PaymentFailedIntegrationEventConsumer</c> tarafından <c>ISender</c> ile tetiklenecek. Kanonik iptal
/// gerekçesi sistemce belirlenir (<c>payment_failed</c>) → command yalnız <paramref name="OrderId"/> taşır.
/// Sonuç <c>bool</c>: iptal uygulandıysa <c>true</c>, idempotent/edge no-op ise <c>false</c>.
/// </summary>
/// <param name="OrderId">İptal edilecek siparişin kimliği.</param>
public sealed record FailOrderPaymentCommand(Guid OrderId) : ICommand<bool>;
