using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Application.Orders.Configuration;

namespace OrderHub.OrderService.Infrastructure.BackgroundJobs;

/// <summary>
/// Startup'ta sweep backstop recurring job'unu kaydeder (idempotent <see cref="IRecurringJobManager.AddOrUpdate"/>
/// → her başlatmada güvenle çağrılır, duplicate üretmez). Tüm ortamlarda çalışır. CRON <b>UTC</b> olarak
/// yorumlanır (ADR-0003 Karar 4). Hangfire storage <c>AddHangfire</c>'da hazır olduğundan kayıt server'ın
/// çalışmasını beklemez.
/// </summary>
internal sealed class RecurringJobRegistrar(
    IRecurringJobManager recurringJobManager,
    IOptions<OrderTimeoutOptions> options)
    : IHostedService
{
    private const string SweepJobId = "sweep-unpaid-orders";
    private const string DailySalesReportJobId = "daily-sales-report";
    private const string LowStockAlertJobId = "low-stock-alert";

    // Recurring CRON'lar sabit (YAGNI — config soyutlaması yok; gerekirse ileride options). UTC yorumlanır.
    private const string DailySalesReportCron = "0 2 * * *";
    private const string LowStockAlertCron = "0 * * * *"; // saatlik (her saat başı).

    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<SweepUnpaidOrdersJob>(
            SweepJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            options.Value.SweepCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        recurringJobManager.AddOrUpdate<DailySalesReportJob>(
            DailySalesReportJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            DailySalesReportCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Faz 2.4 placeholder — InventoryService Faz 5'te gelince gerçek implementasyon (ROADMAP §2.4).
        recurringJobManager.AddOrUpdate<LowStockAlertJob>(
            LowStockAlertJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            LowStockAlertCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
