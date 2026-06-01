using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.PaymentService.Application.Abstractions.Payments;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Application.Common.Logging;
using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.Application.Payments.Commands.ProcessPayment;

/// <summary>
/// <see cref="ProcessPaymentCommand"/> handler'ı: Payment aggregate'ini oluşturur, mock sağlayıcıyı çağırır
/// ve sonucu aggregate'e yazar (MarkSucceeded/MarkFailed → domain olayı yükselir). Her iki sonuçta da ödeme
/// <b>kaydedilir</b> (girişim kalıcı olur) ve command başarılı döner — ödeme sonucu domain olayında taşınır
/// (3c'de outbox ile yayımlanacak). <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class ProcessPaymentCommandHandler(
    IPaymentRepository repository,
    IPaymentProcessor processor,
    ILogger<ProcessPaymentCommandHandler> logger)
    : IRequestHandler<ProcessPaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = Payment.Create(request.OrderId, request.Amount, request.Currency);

        var result = await processor.ProcessAsync(
            request.OrderId, request.Amount, request.Currency, cancellationToken);

        if (result.IsSuccess)
        {
            payment.MarkSucceeded(result.ExternalTransactionId!);
            ApplicationLog.PaymentSucceeded(logger, payment.Id, payment.OrderId);
        }
        else
        {
            payment.MarkFailed(result.FailureReason ?? "Payment was declined.");
            ApplicationLog.PaymentFailed(logger, payment.Id, payment.OrderId);
        }

        repository.Add(payment);

        return Result.Success(payment.Id);
    }
}
