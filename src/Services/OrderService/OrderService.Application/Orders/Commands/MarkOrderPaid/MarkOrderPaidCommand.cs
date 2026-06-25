using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

/// <summary>
/// Bir siparişi ödenmiş işaretler (PaymentSucceeded akışı). 3c-3'te
/// <c>PaymentSucceededIntegrationEventConsumer</c> tarafından <c>ISender</c> ile tetiklenecek; HTTP ile değil.
/// Sonuç <c>bool</c>: geçiş uygulandıysa <c>true</c>, idempotent/edge no-op ise <c>false</c>.
/// </summary>
/// <param name="OrderId">Ödenmiş işaretlenecek siparişin kimliği.</param>
public sealed record MarkOrderPaidCommand(Guid OrderId) : ICommand<bool>;
