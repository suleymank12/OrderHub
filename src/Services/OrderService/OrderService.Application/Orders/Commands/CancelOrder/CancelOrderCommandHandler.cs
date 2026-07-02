using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.CancelOrder;

/// <summary>
/// <see cref="CancelOrderCommand"/> handler'ı. <b>Idempotent</b> (at-least-once saga-command teslimatı): order
/// yüklenir, status guard'lanır — Pending/Confirmed ise <see cref="Order.Cancel"/> saga'nın verdiği gerekçeyle;
/// zaten Cancelled ise no-op + log (<c>Success(false)</c>, redelivery). Paid/Shipped edge (telafi buraya ULAŞMAZ —
/// AwaitingStockConfirmation forward-only, refund kapsam dışı) → <c>Failure</c> (consumer ack'ler, retry gerekmez).
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class CancelOrderCommandHandler(
    IOrderRepository repository,
    ILogger<CancelOrderCommandHandler> logger)
    : IRequestHandler<CancelOrderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<bool>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        // Idempotent redelivery: zaten Cancelled → no-op (ack, geçiş uygulanmadı).
        if (order.Status == OrderStatus.Cancelled)
        {
            ApplicationLog.OrderCancelSkipped(logger, order.Id, order.Status);
            return Result.Success(false);
        }

        // Paid/Shipped edge: iptal edilemez (refund/compensation gerektirir, kapsam dışı). Telafi bu state'lere
        // ulaşmaz; savunmacı → Failure (consumer ack'ler; retry düzeltmez, bu terminal bir durum).
        if (order.Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
        {
            ApplicationLog.OrderCancelSkipped(logger, order.Id, order.Status);
            return Result.Failure<bool>(Error.Conflict(
                "Order.CannotCancel", $"Order '{order.Id}' in status '{order.Status}' cannot be cancelled."));
        }

        order.Cancel(request.Reason);
        ApplicationLog.OrderCancelledByCommand(logger, order.Id, request.Reason);

        return Result.Success(true);
    }
}
