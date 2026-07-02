using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

/// <summary>
/// <see cref="MarkOrderPaidCommand"/> handler'ı. <b>Idempotent</b> (at-least-once teslimat): order yüklenir,
/// status guard'lanır. Faz 5 saga (Karar D) refinement'ı — <c>status</c> ayrımı:
/// <list type="bullet">
/// <item><b>Pending</b> → <see cref="MarkOrderPaidCommand.NotYetConfirmedErrorCode"/> kodlu <b>retryable
/// failure</b>: ConfirmOrder bu siparişe henüz ulaşmadı (saga ConfirmOrder + ProcessPayment'ı paralel gönderir).
/// Failure dönmek KRİTİK: <c>TransactionBehavior</c> failure'da <c>SaveChanges</c> YAPMAZ → inbox satırı commit
/// olmaz → consumer throw'u sonrası retry GERÇEKTEN yeniden çalışır (Success(false) olsaydı inbox commit edilir,
/// retry skip edilir, MarkOrderPaid kaybolurdu).</item>
/// <item><b>Paid/Shipped/Cancelled</b> (terminal) → idempotent/edge no-op (<c>Success(false)</c>, ack — retry yok).</item>
/// <item><b>Confirmed</b> → <see cref="Order.MarkPaid"/> → <c>Success(true)</c>.</item>
/// </list>
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class MarkOrderPaidCommandHandler(
    IOrderRepository repository,
    ILogger<MarkOrderPaidCommandHandler> logger)
    : IRequestHandler<MarkOrderPaidCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(MarkOrderPaidCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<bool>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        // Pending → ConfirmOrder bu siparişe henüz ulaşmadı (saga Karar D paralel send). Retryable failure:
        // inbox commit ETME → consumer throw → retry → ConfirmOrder işlenince Confirmed → başarı.
        if (order.Status == OrderStatus.Pending)
        {
            ApplicationLog.OrderMarkPaidPendingRetry(logger, order.Id);
            return Result.Failure<bool>(Error.Conflict(
                MarkOrderPaidCommand.NotYetConfirmedErrorCode,
                $"Order '{order.Id}' is still Pending; ConfirmOrder not yet applied (retry)."));
        }

        // Terminal/non-Confirmed (Paid/Shipped/Cancelled) → güvenli no-op (idempotency/edge), ack.
        if (order.Status != OrderStatus.Confirmed)
        {
            ApplicationLog.OrderMarkPaidSkipped(logger, order.Id, order.Status);
            return Result.Success(false);
        }

        order.MarkPaid();
        ApplicationLog.OrderMarkedPaid(logger, order.Id);

        return Result.Success(true);
    }
}
