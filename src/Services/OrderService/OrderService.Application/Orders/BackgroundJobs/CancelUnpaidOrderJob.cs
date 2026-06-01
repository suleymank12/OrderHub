using Microsoft.Extensions.Logging;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Gecikmeli job: ödeme süresi dolan Pending siparişi iptal eder (ana yol — ADR-0003 hibrit).
/// <b>Idempotent</b>: sipariş yoksa veya artık Pending değilse (Confirmed/Paid/Cancelled) no-op. Hangfire
/// at-least-once teslimat + sweep backstop nedeniyle aynı job birden çok kez çalışabilir; status guard bunu
/// güvenli kılar (<see cref="Order.Cancel"/> Pending dışında throw eder → guard'a güveniriz, exception'a değil).
/// Hangfire'a bağlı DEĞİL: saf Application servisi (C1). Retry, Infrastructure'daki global filtre ile.
/// </summary>
internal sealed class CancelUnpaidOrderJob(
    IOrderRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<CancelUnpaidOrderJob> logger)
{
    public async Task ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(orderId, cancellationToken);

        if (order is null || order.Status != OrderStatus.Pending)
        {
            ApplicationLog.UnpaidOrderCancellationSkipped(logger, orderId, order?.Status);
            return;
        }

        order.Cancel(OrderCancellationReasons.PaymentTimeout);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ApplicationLog.UnpaidOrderCancelled(logger, orderId);
    }
}
