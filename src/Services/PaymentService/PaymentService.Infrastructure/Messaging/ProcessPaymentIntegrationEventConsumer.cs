using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Payments;
using OrderHub.PaymentService.Application.Payments.Commands.ProcessPayment;

namespace OrderHub.PaymentService.Infrastructure.Messaging;

/// <summary>
/// <see cref="ProcessPaymentIntegrationEvent"/> tüketicisi — İNCE adapter: mesajı
/// <see cref="ProcessPaymentCommand"/>'e map'leyip <see cref="ISender"/> ile tetikler (mevcut handler +
/// pipeline behavior'ları + outbox zinciri yeniden kullanılır; doğrudan repository erişimi YOK, DIP).
/// <para>
/// <b>Sonuç/hata politikası:</b> Başarısız ödeme zaten <b>normal akış</b>tır (handler
/// <c>Payment.MarkFailed</c> → <c>Result.Success</c>) → ack. <c>Result.Failure</c> (beklenmeyen iş hatası)
/// ve <see cref="ValidationException"/> (bozuk/poison mesaj) retry ile düzelmez → <b>log + ack</b> (sonsuz
/// retry / DLQ taşması önlenir). Transient hata (DB/broker down) <b>yakalanmaz</b> → propagate → MassTransit
/// retry (3d retry/DLQ politikası bunun üstüne biner).
/// </para>
/// </summary>
internal sealed partial class ProcessPaymentIntegrationEventConsumer(
    ISender sender,
    ILogger<ProcessPaymentIntegrationEventConsumer> logger)
    : IConsumer<ProcessPaymentIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ProcessPaymentIntegrationEvent> context)
    {
        var message = context.Message;
        var command = new ProcessPaymentCommand(message.OrderId, message.Amount, message.Currency);

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

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning,
        Message = "ProcessPayment failed for order {OrderId} with {ErrorCode}; acked (not retryable)")]
    private static partial void ResultFailureAcked(ILogger logger, Guid orderId, string errorCode);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "ProcessPayment message for order {OrderId} is invalid; acked as poison ({Reason})")]
    private static partial void PoisonMessageAcked(ILogger logger, Guid orderId, string reason);
}
