using Microsoft.EntityFrameworkCore;
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
    : DbContext(options), IUnitOfWork, IOutboxDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (Payment) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        // OutboxMessageConfiguration FARKLI assembly'de (OrderHub.Outbox) → assembly-scan onu YAKALAMAZ;
        // explicit uygulanmalı, aksi halde migration OutboxMessages tablosunu üretmez (Adım 2 dersi).
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
