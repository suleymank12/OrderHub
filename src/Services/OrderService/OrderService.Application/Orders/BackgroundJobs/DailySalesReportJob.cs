using Microsoft.Extensions.Logging;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;

namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Recurring job: önceki günün siparişlerini para birimi bazında özetler ve Seq'e structured log basar.
/// Her gece 02:00 UTC çalışır (ADR-0003 Karar 4: UTC). Faz 4'te bu özet Kafka'ya event olarak yayımlanacak —
/// şimdilik log yeterli (ROADMAP §2.3); Kafka'ya HİÇ dokunulmaz. Saf Application servisi (Hangfire bilmez).
/// Idempotent: yalnızca okur + log basar, yan etki üretmez (Faz 4 Kafka publish gelince dedup gerekecek).
/// </summary>
internal sealed class DailySalesReportJob(
    IOrderSalesQuery salesQuery,
    ILogger<DailySalesReportJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Önceki tam UTC gün: [dün 00:00, bugün 00:00). UtcNow.Date Kind=Utc korur → gün sınırı net.
        var todayUtc = DateTime.UtcNow.Date;
        var fromUtc = todayUtc.AddDays(-1);
        var reportDate = DateOnly.FromDateTime(fromUtc);

        var sales = await salesQuery.GetDailySalesByCurrencyAsync(fromUtc, todayUtc, cancellationToken);

        if (sales.Count == 0)
        {
            ApplicationLog.DailySalesReportEmpty(logger, reportDate);
            return;
        }

        foreach (var line in sales)
        {
            ApplicationLog.DailySalesReportLine(logger, reportDate, line.Currency, line.OrderCount, line.Revenue);
        }
    }
}
