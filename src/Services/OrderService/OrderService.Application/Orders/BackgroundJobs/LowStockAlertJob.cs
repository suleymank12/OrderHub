using Microsoft.Extensions.Logging;
using OrderHub.OrderService.Application.Common.Logging;

namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Saatlik recurring <b>placeholder</b>: stok eşik uyarısı. InventoryService Faz 5'te geleceğinden ŞİMDİ
/// yalnızca "entegre edilmedi" log'u atar — var olmayan servise call atılamaz (ROADMAP §2.4). Bu K2 ihlali
/// DEĞİLDİR: dokümante edilmiş sınır ve replacement planı kayıtlı.
/// <para>
/// <b>Faz 5 evrim:</b> InventoryService eklenince bu job <c>IInventoryQuery.GetLowStockItemsAsync()</c>
/// çağıracak ve düşük stok alert'lerini event olarak yayımlayacak (Outbox/Kafka). Saf Application servisi
/// (Hangfire bilmez).
/// </para>
/// </summary>
internal sealed class LowStockAlertJob(ILogger<LowStockAlertJob> logger)
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationLog.LowStockAlertSkipped(logger);

        return Task.CompletedTask;
    }
}
