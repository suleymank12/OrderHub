using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;

namespace OrderHub.AnalyticsService.Application.Abstractions.Persistence;

/// <summary>
/// Read-model projection'ları için read-side persistence portu. Application'da tanımlı, Infrastructure EF Core
/// ile (read-only, <c>AsNoTracking</c>) implement eder (DIP) → Application <c>AnalyticsDbContext</c>'i
/// (Infrastructure) bilmez (Clean Arch). Yalnız okuma; <c>IQueryable</c> sızdırmaz → materialize edilmiş
/// sonuç döner (§9). Write yoktur (CQRS read-side, ADR-0006).
/// </summary>
public interface IAnalyticsReadRepository
{
    /// <summary>Id ile sipariş projection'ını yükler; bulunamazsa <c>null</c> döner.</summary>
    Task<OrderProjection?> GetOrderProjectionByIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="from"/>–<paramref name="to"/> (dahil) aralığındaki günlük gelir satırlarını tarih
    /// sırasıyla döner. Aralıkta veri yoksa boş liste (başarılı sonuç).
    /// </summary>
    Task<IReadOnlyList<DailyRevenueProjection>> GetDailyRevenueAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
