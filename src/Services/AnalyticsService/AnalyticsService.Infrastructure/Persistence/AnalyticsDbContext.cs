using Microsoft.EntityFrameworkCore;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.Inbox;
using OrderHub.Inbox.Persistence;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence;

/// <summary>
/// AnalyticsService'in EF Core <see cref="DbContext"/>'i — read-model projection'ları (CQRS read-side, ADR-0006)
/// + consumer-side idempotency dedup tablosu (<see cref="InboxMessage"/>, OrderHub.Inbox ENTITY'si reuse;
/// MassTransit filter DEĞİL). Kendi DB'si: <c>OrderHub_Analytics</c>. <see cref="IInboxDbContext"/> implement eder →
/// consumer projection apply + dedup kaydını <b>tek SaveChanges</b>'te atomik yazar. Outbox/MassTransit YOK.
/// </summary>
internal sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : DbContext(options), IInboxDbContext
{
    public DbSet<OrderProjection> OrderProjections => Set<OrderProjection>();

    public DbSet<DailyRevenueProjection> DailyRevenueProjections => Set<DailyRevenueProjection>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);

        // Inbox config FARKLI assembly'de (building block) → assembly-scan yakalamaz; explicit uygulanır.
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
