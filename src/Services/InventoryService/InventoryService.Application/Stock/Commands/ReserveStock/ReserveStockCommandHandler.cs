using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Common.Logging;
using OrderHub.InventoryService.Domain.Stock.Exceptions;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReserveStock;

/// <summary>
/// <see cref="ReserveStockCommand"/> handler'ı: ürüne ait stok kalemini yükler ve rezervasyon dener.
/// <b>Yeterli stok:</b> <c>StockItem.Reserve</c> çağrılır → <c>StockReserved</c> domain olayı yükseltilir
/// → outbox <c>StockReservedIntegrationEvent</c>'i saga'ya iletir.
/// <b>Yetersiz stok:</b> <see cref="InsufficientStockException"/> yakalanır →
/// <c>StockItem.RecordReservationFailure</c> çağrılır → <c>StockReservationFailed</c> domain olayı yükseltilir
/// → outbox compensation sinyalini saga'ya iletir.
/// Her iki durumda da handler <c>Result.Success()</c> döner: yetersiz stok NORMAL iş sonucudur,
/// exception yalnız aggregate invariant'ını korur ve sızdırılmaz; TransactionBehavior committer,
/// domain olayı outbox ile atomik yazılır (ADR-0007 Karar 2).
/// <c>SaveChanges</c>'i TransactionBehavior yönetir (tek commit boundary).
/// </summary>
internal sealed class ReserveStockCommandHandler(
    IInventoryRepository repository,
    ILogger<ReserveStockCommandHandler> logger)
    : IRequestHandler<ReserveStockCommand, Result>
{
    /// <summary>
    /// Saga'da rezervasyon geçerlilik süresi: stok 15 dakika boyunca ayrılır. Süre dolduktan sonra
    /// Hangfire job'u süresi dolmuş Pending rezervasyonları expire eder (ADR-0007 Karar 2).
    /// </summary>
    private static readonly TimeSpan ReservationDuration = TimeSpan.FromMinutes(15);

    public async Task<Result> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (item is null)
        {
            ApplicationLog.StockItemNotFound(logger, request.OrderId, request.ProductId);
            return Result.Failure(Error.NotFound(
                "Inventory.StockItemNotFound",
                $"Stock item for product {request.ProductId} was not found."));
        }

        try
        {
            item.Reserve(request.OrderId, request.Quantity, DateTime.UtcNow.Add(ReservationDuration));
            ApplicationLog.StockReserved(logger, request.OrderId, request.ProductId, request.Quantity);
        }
        catch (InsufficientStockException exception)
        {
            // Yetersiz stok: aggregate invariant koruması yakalanır, sızdırılmaz. RecordReservationFailure
            // StockReservationFailed domain olayını yükseltir → outbox saga'ya compensation sinyali gönderir.
            item.RecordReservationFailure(request.OrderId, exception.Message);
            ApplicationLog.StockReservationFailed(logger, request.OrderId, request.ProductId, exception.Message);
        }

        return Result.Success();
    }
}
