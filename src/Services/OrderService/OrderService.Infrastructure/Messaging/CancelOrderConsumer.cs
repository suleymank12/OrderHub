using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application.Orders.Commands.CancelOrder;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="CancelOrderIntegrationEvent"/> tüketicisi (saga → OrderService, Faz 5e compensation) — İNCE adapter:
/// mesajı <see cref="CancelOrderCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler (mevcut handler +
/// behavior'lar + idempotency guard yeniden kullanılır; DIP). Saga telafi dalında (5e-2) gönderir; bu adımda DORMANT.
/// <para>
/// İptal <b>forward</b> telafi adımıdır → retry gerektirmez; TÜM sonuçlar <b>ack</b>. <c>Success(true)</c> (iptal
/// edildi) / <c>Success(false)</c> (idempotent, zaten Cancelled) → ack. <c>Result.Failure</c> (NotFound veya
/// Paid/Shipped edge conflict) retry ile düzelmez → ack (<see cref="MarkOrderPaidConsumer"/>'ın Karar D retry-throw'u
/// BURADA YOK). <see cref="ValidationException"/> → log + ack (poison). Transient hata <b>yakalanmaz</b> → propagate → retry.
/// </para>
/// </summary>
internal sealed partial class CancelOrderConsumer(
    ISender sender,
    ILogger<CancelOrderConsumer> logger)
    : IConsumer<CancelOrderIntegrationEvent>
{
    public async Task Consume(ConsumeContext<CancelOrderIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(
                new CancelOrderCommand(message.OrderId, message.Reason), context.CancellationToken);

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

    [LoggerMessage(EventId = 4500, Level = LogLevel.Warning,
        Message = "Cancel-order failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4501, Level = LogLevel.Warning,
        Message = "CancelOrder message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
