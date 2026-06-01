using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Payments;
using OrderHub.OrderService.Application.Orders.Commands.FailOrderPayment;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="PaymentFailedIntegrationEvent"/> tüketicisi — İNCE adapter: mesajı
/// <see cref="FailOrderPaymentCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler. Hata politikası
/// <see cref="PaymentSucceededIntegrationEventConsumer"/> ile AYNI: <c>Result.Failure</c> (NotFound dahil)
/// ve <see cref="ValidationException"/> → log + ack; transient hata → propagate → retry. Aynı mesaj iki kez
/// gelirse handler'ın status guard'ı no-op yapar (idempotent).
/// </summary>
internal sealed partial class PaymentFailedIntegrationEventConsumer(
    ISender sender,
    ILogger<PaymentFailedIntegrationEventConsumer> logger)
    : IConsumer<PaymentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(new FailOrderPaymentCommand(message.OrderId), context.CancellationToken);

            if (result.IsFailure)
            {
                ResultFailureAcked(logger, message.OrderId, result.Error.Code);
            }
        }
        catch (ValidationException exception)
        {
            PoisonMessageAcked(logger, message.OrderId, exception.Message);
        }

        // Transient exception'lar bilinçli olarak YAKALANMAZ → propagate → retry.
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning,
        Message = "Fail-order-payment failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning,
        Message = "PaymentFailed message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
