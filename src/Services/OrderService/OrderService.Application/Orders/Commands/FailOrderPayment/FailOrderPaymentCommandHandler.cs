using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.FailOrderPayment;

/// <summary>
/// <see cref="FailOrderPaymentCommand"/> handler'ı. <b>Idempotent</b> (at-least-once): order yüklenir, status
/// guard'lanır — iptal edilebilir durumdaysa (Pending/Confirmed) <see cref="Order.Cancel"/> kanonik gerekçe
/// (<see cref="OrderCancellationReasons.PaymentFailed"/>) ile; değilse (zaten Cancelled, ya da Paid/Shipped
/// edge) no-op + log (throw YOK; telafi Faz 5 saga). Commit'i TransactionBehavior yönetir.
/// </summary>
internal sealed class FailOrderPaymentCommandHandler(
    IOrderRepository repository,
    ILogger<FailOrderPaymentCommandHandler> logger)
    : IRequestHandler<FailOrderPaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(FailOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<bool>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        // İptal yalnız Pending/Confirmed'den geçerli; diğer durumlar (zaten Cancelled veya Paid edge) → no-op.
        if (order.Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
        {
            ApplicationLog.OrderPaymentFailureSkipped(logger, order.Id, order.Status);
            return Result.Success(false);
        }

        order.Cancel(OrderCancellationReasons.PaymentFailed);
        ApplicationLog.OrderCancelledByPaymentFailure(logger, order.Id);

        return Result.Success(true);
    }
}
