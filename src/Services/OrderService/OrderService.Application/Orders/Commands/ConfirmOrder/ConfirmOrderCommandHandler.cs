using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.ConfirmOrder;

/// <summary>
/// <see cref="ConfirmOrderCommand"/> handler'ı. <b>Idempotent</b> (at-least-once saga-command teslimatı): order
/// yüklenir, status guard'lanır — yalnızca Pending ise <see cref="Order.Confirm"/>; aksi halde (zaten Confirmed
/// = redelivery, ya da Paid/Shipped/Cancelled edge) no-op + log (throw YOK → poison message üretme). Guard,
/// aggregate'in <c>OrderAlreadyConfirmedException</c>'ını handler katmanında önler (<see cref="MarkOrderPaid"/>
/// deseniyle aynı). <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class ConfirmOrderCommandHandler(
    IOrderRepository repository,
    ILogger<ConfirmOrderCommandHandler> logger)
    : IRequestHandler<ConfirmOrderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ConfirmOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<bool>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        // Pending → Confirmed yalnızca; zaten Confirmed (idempotent redelivery) veya ileri/terminal → no-op.
        if (order.Status != OrderStatus.Pending)
        {
            ApplicationLog.OrderConfirmSkipped(logger, order.Id, order.Status);
            return Result.Success(false);
        }

        order.Confirm();
        ApplicationLog.OrderConfirmedByCommand(logger, order.Id);

        return Result.Success(true);
    }
}
