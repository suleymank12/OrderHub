using Microsoft.EntityFrameworkCore;
using OrderHub.Inbox;
using OrderHub.Inbox.Persistence;
using OrderHub.Outbox;
using OrderHub.Outbox.Persistence;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.Infrastructure.Persistence;

/// <summary>
/// PaymentService'in EF Core <see cref="DbContext"/>'i; <see cref="IUnitOfWork"/> ve <see cref="IOutboxDbContext"/>
/// implementasyonu. <see cref="OutboxMessages"/> domain ile <b>aynı DB/transaction</b>'dadır (database-per-service)
/// → pre-commit interceptor outbox satırını atomik yazar (ADR-0002 Faz 3 Karar 1). Kendi DB'si: OrderHub_Payment.
/// </summary>
internal sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IUnitOfWork, IOutboxDbContext, IInboxDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (Payment) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        // Outbox/Inbox configuration'ları FARKLI assembly'lerde (building block) → assembly-scan YAKALAMAZ;
        // explicit uygulanmalı, aksi halde migration tabloları üretmez.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
