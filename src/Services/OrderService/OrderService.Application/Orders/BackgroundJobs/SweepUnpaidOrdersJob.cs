using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Application.Orders.Configuration;

namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Reconciliation backstop (recurring): <see cref="OrderTimeoutOptions.UnpaidTimeout"/>'tan daha eski, hâlâ
/// Pending siparişleri toplu iptal eder. Per-order gecikmeli job'un kaçırdığı durumları (schedule edilemeyen,
/// server crash, drift, commit↔enqueue penceresi) yakalar (ADR-0003 hibrit). Idempotent: sorgu yalnızca
/// Pending döndüğünden zaten iptal olan tekrar işlenmez. Saf Application servisi (Hangfire bilmez).
/// </summary>
internal sealed class SweepUnpaidOrdersJob(
    IOrderRepository repository,
    IUnitOfWork unitOfWork,
    IOptions<OrderTimeoutOptions> options,
    ILogger<SweepUnpaidOrdersJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow - options.Value.UnpaidTimeout;

        var staleOrders = await repository.GetPendingOlderThanAsync(cutoffUtc, cancellationToken);
        if (staleOrders.Count == 0)
        {
            return;
        }

        foreach (var order in staleOrders)
        {
            order.Cancel(OrderCancellationReasons.PaymentTimeout);
        }

        // Toplu commit: bir order eşzamanlı iptal edilirse RowVersion concurrency throw eder → Hangfire
        // retry'da sorgu onu artık Pending görmez (self-healing). Tek-instance Faz 2'de pratikte yarış yok.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ApplicationLog.UnpaidOrdersSwept(logger, staleOrders.Count, cutoffUtc);
    }
}
