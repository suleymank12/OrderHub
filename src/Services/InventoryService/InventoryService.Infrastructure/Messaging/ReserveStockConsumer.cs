using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Inventory;
using OrderHub.InventoryService.Application.Stock.Commands.ReserveStock;

namespace OrderHub.InventoryService.Infrastructure.Messaging;

/// <summary>
/// ReserveStockIntegrationEvent tüketicisi — ince adapter: mesajı ReserveStockCommand'e
/// map'leyip ISender ile tetikler (mevcut handler + pipeline behavior'ları yeniden kullanılır;
/// doğrudan repository erişimi YOK, DIP).
/// <para>
/// Sonuç/hata politikası: Result.Failure (beklenmeyen iş hatası) ve ValidationException
/// (bozuk/poison mesaj) retry ile düzelmez — log + ack. Transient hata (DB/broker down)
/// yakalanmaz — propagate — MassTransit retry (3d retry/DLQ politikası bunun üstüne biner).
/// </para>
/// </summary>
internal sealed partial class ReserveStockConsumer(
    ISender sender,
    ILogger<ReserveStockConsumer> logger)
    : IConsumer<ReserveStockIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ReserveStockIntegrationEvent> context)
    {
        var message = context.Message;
        var command = new ReserveStockCommand(message.OrderId, message.ProductId, message.Quantity);

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

    [LoggerMessage(EventId = 5000, Level = LogLevel.Warning,
        Message = "ReserveStock failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning,
        Message = "ReserveStock message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
