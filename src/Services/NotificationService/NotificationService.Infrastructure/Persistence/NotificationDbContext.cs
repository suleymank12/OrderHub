using Microsoft.EntityFrameworkCore;
using OrderHub.Inbox;
using OrderHub.Inbox.Persistence;
using OrderHub.NotificationService.Domain.Orders;

namespace OrderHub.NotificationService.Infrastructure.Persistence;

/// <summary>
/// NotificationService'in EF Core <see cref="DbContext"/>'i — sipariş read-model projection'ı (Kafka order-stream
/// consumer tarafından güncellenir) + consumer-side idempotency dedup tablosu (<see cref="InboxMessage"/>,
/// OrderHub.Inbox entity'si reuse). Kendi DB'si: <c>OrderHub_Notifications</c>. <see cref="IInboxDbContext"/>
/// implement eder → consumer projection apply + dedup kaydını <b>tek SaveChanges</b>'te atomik yazar.
/// Outbox/MassTransit/Revenue YOK (terminal consumer, bildirim read-side).
/// </summary>
internal sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IInboxDbContext
{
    public DbSet<OrderProjection> OrderProjections => Set<OrderProjection>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);

        // Inbox config FARKLI assembly'de (building block) → assembly-scan yakalamaz; explicit uygulanır.
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
