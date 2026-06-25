using Microsoft.EntityFrameworkCore;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence;

/// <summary>
/// AnalyticsService'in EF Core <see cref="DbContext"/>'i — yalnız <b>read-model</b> projection'larını tutar
/// (CQRS read-side, ADR-0006). Kendi DB'si: <c>OrderHub_Analytics</c> (database-per-service). Outbox/Inbox YOK
/// (Analytics event üretmez, terminal consumer; Kafka offset + idempotency 4c-2/4c-3'te). Aggregate yok.
/// </summary>
internal sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<OrderProjection> OrderProjections => Set<OrderProjection>();

    public DbSet<DailyRevenueProjection> DailyRevenueProjections => Set<DailyRevenueProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
