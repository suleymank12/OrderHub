using OrderHub.AnalyticsService.Application.Abstractions.Messaging;
using OrderHub.AnalyticsService.Application.Revenue.Dtos;

namespace OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;

/// <summary>
/// Bir tarih aralığındaki günlük gelir satırlarını getirir (her ikisi dahil). <see cref="From"/>/<see cref="To"/>
/// tutarlılığı <see cref="GetDailyRevenueQueryValidator"/> ile zorlanır (From &lt;= To). Aralıkta veri yoksa boş
/// liste (başarılı sonuç).
/// </summary>
/// <param name="From">Aralığın başlangıç günü (dahil).</param>
/// <param name="To">Aralığın bitiş günü (dahil).</param>
public sealed record GetDailyRevenueQuery(DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<DailyRevenueDto>>;
