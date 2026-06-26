using Microsoft.Extensions.Logging;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Common.Logging;
using OrderHub.InventoryService.Domain.Stock;

namespace OrderHub.InventoryService.Application.Stock.BackgroundJobs;

/// <summary>
/// Reconciliation backstop (recurring): <c>ExpiresAtUtc</c>'si geçmiş Pending rezervasyonları toplu
/// olarak expire eder. Hangfire job'unun kaydettiği individual per-reservation job'ların kaçırdığı
/// durumları (schedule edilemeyen, server crash, drift, commit↔enqueue penceresi) yakalar.
/// Idempotent: sorguda yalnız Pending + süresi dolmuş rezervasyonlar döner; zaten Expired olan
/// tekrar işlenmez. Saf Application servisi → Hangfire bilmez; Infrastructure/Api katmanında
/// <c>AddScoped</c> + Hangfire recurring job olarak kayıtlanır (5c devamı).
/// </summary>
internal sealed class ExpireStockReservationsJob(
    IInventoryRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<ExpireStockReservationsJob> logger)
{
    /// <summary>
    /// Süresi dolmuş tüm Pending rezervasyonları expire eder ve tek commit'te atomik olarak yazar.
    /// Her <c>ExpireReservation</c> çağrısı <c>StockReservationExpired</c> domain olayı yükseltir;
    /// outbox interceptor bu olayları aynı transaction'da outbox tablosuna yazar →
    /// saga'ya expiry sinyali atomik teslim edilir (ADR-0007 Karar 2).
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var items = await repository.GetWithExpiredReservationsAsync(nowUtc, cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        var expiredCount = 0;

        foreach (var item in items)
        {
            foreach (var reservation in item.Reservations
                .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAtUtc < nowUtc)
                .ToList())
            {
                item.ExpireReservation(reservation.OrderId);
                expiredCount++;
            }
        }

        // Toplu commit: aynı anda Confirm gelen rezervasyon InvalidReservationStatusTransitionException fırlatır
        // → Hangfire retry'da sorgu onu artık Pending görmez (self-healing). Tek-instance Faz 5'te
        // pratikte yarış nadirdir; optimistic concurrency (RowVersion) ikinci güvence katmanıdır.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ApplicationLog.StockReservationsExpired(logger, expiredCount);
    }
}
