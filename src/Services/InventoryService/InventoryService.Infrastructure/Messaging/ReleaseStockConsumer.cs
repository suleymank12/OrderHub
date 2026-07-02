using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Inventory;
using OrderHub.InventoryService.Application.Stock.Commands.ReleaseStock;

namespace OrderHub.InventoryService.Infrastructure.Messaging;

/// <summary>
/// ReleaseStockIntegrationEvent tüketicisi — ince adapter: mesajı ReleaseStockCommand'e
/// map'leyip ISender ile tetikler (compensation path: ödeme başarısız veya sipariş iptal).
/// <para>
/// Sonuç/hata politikası: Result.Failure ve ValidationException (poison mesaj) retry ile
/// düzelmez — log + ack. Transient hatalar yakalanmaz — propagate — MassTransit retry.
/// </para>
/// </summary>
internal sealed partial class ReleaseStockConsumer(
    ISender sender,
    ILogger<ReleaseStockConsumer> logger)
    : IConsumer<ReleaseStockIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ReleaseStockIntegrationEvent> context)
    {
        var message = context.Message;
        var command = new ReleaseStockCommand(message.OrderId, message.ProductId);

        try
        {
            var result = await sender.Send(command, context.CancellationToken);

            if (result.IsFailure)
            {
                // Beklenen iş hatası: retry düzeltmez → ack + log.
                ResultFailureAcked(logger, message.OrderId, result.Error.Code);
            }
        }
        catch (ValidationException exception)
        {
            // Poison message (bozuk input): retry düzeltmez → ack + log.
            PoisonMessageAcked(logger, message.OrderId, exception.Message);
        }

        // Diğer exception'lar (transient: DB/broker down) bilinçli olarak YAKALANMAZ → propagate → retry.
    }

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning,
        Message = "ReleaseStock failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning,
        Message = "ReleaseStock message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
