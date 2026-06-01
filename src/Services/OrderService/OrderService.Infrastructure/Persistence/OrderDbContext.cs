using Microsoft.EntityFrameworkCore;
using OrderHub.Inbox;
using OrderHub.Inbox.Persistence;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.Outbox;
using OrderHub.Outbox.Persistence;

namespace OrderHub.OrderService.Infrastructure.Persistence;

/// <summary>
/// OrderService'in EF Core <see cref="DbContext"/>'i; <see cref="IUnitOfWork"/> ve <see cref="IOutboxDbContext"/>
/// implementasyonu. <see cref="IUnitOfWork.SaveChangesAsync"/> doğrudan <see cref="DbContext"/>'in inherited
/// metodu ile karşılanır → repository ile aynı scope/instance paylaşıldığında biriken değişiklikler atomik
/// kalıcılaşır. <see cref="Order"/> aggregate root'u DbSet olarak expose edilir (kalemlere yalnızca aggregate
/// üzerinden erişilir, aggregate boundary). <see cref="OutboxMessages"/> outbox tablosudur: domain ile <b>aynı
/// DB/transaction</b> (database-per-service) → pre-commit interceptor atomik yazar (ADR-0002 Faz 3 Karar 1).
/// </summary>
internal sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : DbContext(options), IUnitOfWork, IOutboxDbContext, IInboxDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (Order, OrderItem) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        // Outbox/Inbox configuration'ları FARKLI assembly'lerde (building block) → assembly-scan YAKALAMAZ;
        // explicit uygulanmalı, aksi halde migration tabloları üretmez.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
