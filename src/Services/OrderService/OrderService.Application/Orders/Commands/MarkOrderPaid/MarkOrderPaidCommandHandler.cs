using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

/// <summary>
/// <see cref="MarkOrderPaidCommand"/> handler'ı. <b>Idempotent</b> (at-least-once teslimat): order yüklenir,
/// status guard'lanır — zaten Paid ise no-op (<c>false</c>); Confirmed değilse (örn. timeout Cancelled etti)
/// edge no-op + log (throw YOK → poison message üretme; tam telafi Faz 5 saga); Confirmed ise
/// <see cref="Order.MarkPaid"/>. <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
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

        // Confirmed → Paid yalnızca; zaten Paid veya başka edge state → güvenli no-op (idempotency/race).
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
