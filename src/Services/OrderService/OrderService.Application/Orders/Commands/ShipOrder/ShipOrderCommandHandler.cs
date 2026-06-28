using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.ShipOrder;

/// <summary>
/// <see cref="ShipOrderCommand"/> handler'ı. <b>Idempotent</b> (at-least-once saga-command teslimatı): order
/// yüklenir, status guard'lanır — yalnızca Paid ise <see cref="Order.Ship"/>; aksi halde (zaten Shipped =
/// redelivery, ya da Confirmed/Pending/Cancelled edge) no-op + log (throw YOK → poison message üretme).
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary). <see cref="MarkOrderPaid"/> deseniyle aynı.
/// </summary>
internal sealed class ShipOrderCommandHandler(
    IOrderRepository repository,
    ILogger<ShipOrderCommandHandler> logger)
    : IRequestHandler<ShipOrderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<bool>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        // Paid → Shipped yalnızca; zaten Shipped (idempotent redelivery) veya başka edge state → güvenli no-op.
        if (order.Status != OrderStatus.Paid)
        {
            ApplicationLog.OrderShipSkipped(logger, order.Id, order.Status);
            return Result.Success(false);
        }

        order.Ship();
        ApplicationLog.OrderShipped(logger, order.Id);

        return Result.Success(true);
    }
}
