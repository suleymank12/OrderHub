using Microsoft.EntityFrameworkCore;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence;

/// <summary>
/// <see cref="IAnalyticsReadRepository"/>'nin EF Core implementasyonu (read-side port, DIP). Tüm sorgular
/// <c>AsNoTracking</c>: read-model salt-okunur, change-tracker'a gerek yok (perf + niyet açık). <c>IQueryable</c>
/// dışarı sızdırmaz → materialize edilmiş sonuç döner (§9). Business logic içermez.
/// </summary>
internal sealed class AnalyticsReadRepository(AnalyticsDbContext context) : IAnalyticsReadRepository
{
    public async Task<OrderProjection?> GetOrderProjectionByIdAsync(
        Guid orderId, CancellationToken cancellationToken) =>
        await context.OrderProjections
            .AsNoTracking()
            .FirstOrDefaultAsync(projection => projection.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<DailyRevenueProjection>> GetDailyRevenueAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await context.DailyRevenueProjections
            .AsNoTracking()
            .Where(revenue => revenue.Date >= from && revenue.Date <= to)
            .OrderBy(revenue => revenue.Date)
            .ToListAsync(cancellationToken);
}
