using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Common.Logging;

namespace OrderHub.InventoryService.Application.Stock.Commands.ConfirmStockReservation;

/// <summary>
/// <see cref="ConfirmStockReservationCommand"/> handler'ı: ürün kimliğiyle stok kalemini yükler,
/// rezervasyonu Pending → Confirmed'a geçirir ve <c>StockReservationConfirmed</c> domain olayını yükseltir.
/// Domain exception'ları (<c>ReservationNotFoundException</c>, <c>InvalidReservationStatusTransitionException</c>)
/// kasıtlı olarak yakalanmaz: bunlar anormaldir (saga tutarsızlığı veya tekrar eden confirm), consumer
/// retry veya DLQ ile çözülür.
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class ConfirmStockReservationCommandHandler(
    IInventoryRepository repository,
    ILogger<ConfirmStockReservationCommandHandler> logger)
    : IRequestHandler<ConfirmStockReservationCommand, Result>
{
    public async Task<Result> Handle(ConfirmStockReservationCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (item is null)
        {
            ApplicationLog.StockItemNotFound(logger, request.OrderId, request.ProductId);
            return Result.Failure(Error.NotFound(
                "Inventory.StockItemNotFound",
                $"Stock item for product {request.ProductId} was not found."));
        }

        item.ConfirmReservation(request.OrderId);
        ApplicationLog.StockReservationConfirmed(logger, request.OrderId, request.ProductId);

        return Result.Success();
    }
}
