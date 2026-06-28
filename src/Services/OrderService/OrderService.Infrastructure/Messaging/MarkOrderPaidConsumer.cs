using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="MarkOrderPaidIntegrationEvent"/> tüketicisi (saga → OrderService, Faz 5) — İNCE adapter: mesajı
/// <see cref="MarkOrderPaidCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler. Saga, <c>PaymentSucceeded</c>
/// alınca gönderir.
/// <para>
/// ★ <b>Karar D retry:</b> sipariş henüz <c>Pending</c> ise (ConfirmOrder paralel gönderildi, daha işlenmedi)
/// handler <see cref="MarkOrderPaidCommand.NotYetConfirmedErrorCode"/> kodlu failure döner → consumer
/// <see cref="OrderNotYetConfirmedException"/> <b>fırlatır</b> → MassTransit retry (ConfirmOrder bu arada işlenir
/// → retry'da Confirmed → Paid). Diğer <c>Result.Failure</c> (NotFound) ve idempotent no-op → <b>ack</b>.
/// <see cref="ValidationException"/> → log + ack (poison). Transient hata <b>yakalanmaz</b> → propagate → retry.
/// </para>
/// </summary>
internal sealed partial class MarkOrderPaidConsumer(
    ISender sender,
    ILogger<MarkOrderPaidConsumer> logger)
    : IConsumer<MarkOrderPaidIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MarkOrderPaidIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(new MarkOrderPaidCommand(message.OrderId), context.CancellationToken);

            if (result.IsFailure)
            {
                if (result.Error.Code == MarkOrderPaidCommand.NotYetConfirmedErrorCode)
                {
                    // Sipariş henüz Pending → retry sinyali (throw). Inbox satırı commit edilmedi (handler Failure
                    // döndü → TransactionBehavior SaveChanges yapmadı) → retry consumer'ı gerçekten yeniden çalıştırır.
                    RetryingNotYetConfirmed(logger, message.OrderId);
                    throw new OrderNotYetConfirmedException(message.OrderId);
                }

                // Diğer failure (NotFound) retry ile düzelmez → ack.
                ResultFailureAcked(logger, message.OrderId, result.Error.Code);
            }
        }
        catch (ValidationException exception)
        {
            PoisonMessageAcked(logger, message.OrderId, exception.Message);
        }

        // Transient exception'lar (ve OrderNotYetConfirmedException) bilinçli olarak YAKALANMAZ → propagate → retry.
    }

    [LoggerMessage(EventId = 4400, Level = LogLevel.Warning,
        Message = "Mark-order-paid failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4401, Level = LogLevel.Warning,
        Message = "MarkOrderPaid message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);

    [LoggerMessage(EventId = 4402, Level = LogLevel.Information,
        Message = "Order {OrderId} not yet confirmed; throwing to retry mark-paid (saga Karar D)")]
    private static partial void RetryingNotYetConfirmed(ILogger logger, Guid orderId);
}
