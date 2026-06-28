using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application.Orders.Commands.ShipOrder;

namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="ShipOrderIntegrationEvent"/> tüketicisi (saga → OrderService, Faz 5) — İNCE adapter: mesajı
/// <see cref="ShipOrderCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler. Saga, tüm rezervasyonlar
/// onaylanınca (StockReservationConfirmed fan-out tamamlanınca) gönderir → sipariş Paid → Shipped.
/// <para>
/// <c>Result.Failure</c> (NotFound) ve idempotent no-op (<c>Success(false)</c>, zaten Shipped) → <b>ack</b>.
/// <see cref="ValidationException"/> → log + ack. Transient hata <b>yakalanmaz</b> → propagate → retry. Aynı
/// mesaj iki kez → aggregate status guard no-op (inbox kalıcı dedup ekler).
/// </para>
/// </summary>
internal sealed partial class ShipOrderConsumer(
    ISender sender,
    ILogger<ShipOrderConsumer> logger)
    : IConsumer<ShipOrderIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ShipOrderIntegrationEvent> context)
    {
        var message = context.Message;

        try
        {
            var result = await sender.Send(new ShipOrderCommand(message.OrderId), context.CancellationToken);

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
        Message = "Ship-order failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4501, Level = LogLevel.Warning,
        Message = "ShipOrder message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
