using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Payments;
using OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="PaymentSucceededIntegrationEvent"/> tüketicisi — İNCE adapter: mesajı
/// <see cref="MarkOrderPaidCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler (mevcut handler +
/// behavior'lar + idempotency guard yeniden kullanılır; doğrudan repository erişimi YOK, DIP). Hata
/// politikası ProcessPaymentIntegrationEventConsumer ile AYNI.
/// <para>
/// <c>Result.Failure</c> (bilinmeyen/silinmiş order = NotFound dahil) retry ile düzelmez → <b>log + ack</b>.
/// <see cref="ValidationException"/> (bozuk mesaj) → <b>log + ack</b> (poison). Transient hata (DB down)
/// <b>yakalanmaz</b> → propagate → MassTransit retry (3d). Aynı mesaj iki kez gelirse handler'ın aggregate
/// status guard'ı no-op yapar (idempotent; inbox 3d kalıcı dedup ekler).
/// </para>
/// </summary>
internal sealed partial class PaymentSucceededIntegrationEventConsumer(
    ISender sender,
    ILogger<PaymentSucceededIntegrationEventConsumer> logger)
    : IConsumer<PaymentSucceededIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(new MarkOrderPaidCommand(message.OrderId), context.CancellationToken);

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

    [LoggerMessage(EventId = 4100, Level = LogLevel.Warning,
        Message = "Mark-order-paid failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning,
        Message = "PaymentSucceeded message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
