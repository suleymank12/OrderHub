using Hangfire;
using Microsoft.Extensions.Hosting;
using OrderHub.InventoryService.Application.Stock.BackgroundJobs;

namespace OrderHub.InventoryService.Infrastructure.BackgroundJobs;

/// <summary>
/// Startup'ta stok rezervasyon expiry recurring job'unu kaydeder (idempotent AddOrUpdate
/// — her başlatmada güvenle çağrılır, duplicate üretmez). Tüm ortamlarda çalışır.
/// CRON UTC olarak yorumlanır (ADR-0003 Karar 4). Hangfire storage AddHangfire'da hazır
/// olduğundan kayıt server'ın çalışmasını beklemez.
/// </summary>
internal sealed class RecurringJobRegistrar(IRecurringJobManager recurringJobManager)
    : IHostedService
{
    private const string ExpireReservationsJobId = "expire-stock-reservations";

    // Her 5 dakikada bir (UTC): 15 dakikalık rezervasyon süresi için yeterli ağ; ne kadar sık
    // olursa recovery o kadar hızlı, ama her 5 dakika çalışma maliyeti ihmal edilebilir (idempotent sorgu).
    // ADR-0003 Karar 4: tüm Hangfire CRON'lar UTC timezone ile tanımlanır.
    private const string ExpireReservationsCron = "*/5 * * * *";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<ExpireStockReservationsJob>(
            ExpireReservationsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            ExpireReservationsCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
