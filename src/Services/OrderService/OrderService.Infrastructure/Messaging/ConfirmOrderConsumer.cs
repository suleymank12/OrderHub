using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application.Orders.Commands.ConfirmOrder;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="ConfirmOrderIntegrationEvent"/> tüketicisi (saga → OrderService, Faz 5) — İNCE adapter: mesajı
/// <see cref="ConfirmOrderCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler (mevcut handler + behavior'lar
/// + idempotency guard yeniden kullanılır; DIP). Saga, tüm stok rezerve edilince gönderir.
/// <para>
/// <c>Result.Failure</c> (NotFound) ve idempotent no-op (<c>Success(false)</c>, zaten Confirmed) retry ile
/// düzelmez → <b>ack</b>. <see cref="ValidationException"/> → log + ack (poison). Transient hata <b>yakalanmaz</b>
/// → propagate → retry. Aynı mesaj iki kez → aggregate status guard no-op (inbox kalıcı dedup ekler).
/// </para>
/// </summary>
internal sealed partial class ConfirmOrderConsumer(
    ISender sender,
    ILogger<ConfirmOrderConsumer> logger)
    : IConsumer<ConfirmOrderIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ConfirmOrderIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(new ConfirmOrderCommand(message.OrderId), context.CancellationToken);

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

    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning,
        Message = "Confirm-order failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning,
        Message = "ConfirmOrder message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
