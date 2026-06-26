using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Common.Logging;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReleaseStock;

/// <summary>
/// <see cref="ReleaseStockCommand"/> handler'ı: ürün kimliğiyle stok kalemini yükler,
/// rezervasyonu Pending → Released'a geçirir, stoku iade eder ve <c>StockReleased</c> domain olayını
/// yükseltir. Domain exception'ları (<c>ReservationNotFoundException</c>,
/// <c>InvalidReservationStatusTransitionException</c>) kasıtlı olarak yakalanmaz: anormaldirler,
/// consumer retry veya DLQ ile çözülür.
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class ReleaseStockCommandHandler(
    IInventoryRepository repository,
    ILogger<ReleaseStockCommandHandler> logger)
    : IRequestHandler<ReleaseStockCommand, Result>
{
    public async Task<Result> Handle(ReleaseStockCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (item is null)
        {
            ApplicationLog.StockItemNotFound(logger, request.OrderId, request.ProductId);
            return Result.Failure(Error.NotFound(
                "Inventory.StockItemNotFound",
                $"Stock item for product {request.ProductId} was not found."));
        }

        item.ReleaseReservation(request.OrderId);
        ApplicationLog.StockReleased(logger, request.OrderId, request.ProductId);

        return Result.Success();
    }
}
