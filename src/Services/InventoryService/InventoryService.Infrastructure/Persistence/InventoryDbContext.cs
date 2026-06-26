using Microsoft.EntityFrameworkCore;
using OrderHub.Inbox;
using OrderHub.Inbox.Persistence;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Domain.Catalog;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.Outbox;
using OrderHub.Outbox.Persistence;

namespace OrderHub.InventoryService.Infrastructure.Persistence;

/// <summary>
/// InventoryService'in EF Core DbContext'i; IUnitOfWork, IOutboxDbContext ve IInboxDbContext
/// implementasyonu. OutboxMessages domain ile aynı DB/transaction'dadır (database-per-service)
/// — pre-commit interceptor outbox satırını atomik yazar (ADR-0002 Faz 3 Karar 1). Kendi DB'si: OrderHub_Inventory.
/// </summary>
internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options), IUnitOfWork, IOutboxDbContext, IInboxDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (Product/StockItem/Reservation) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        // Outbox/Inbox configuration'ları FARKLI assembly'lerde (building block) → assembly-scan YAKALAMAZ;
        // explicit uygulanmalı, aksi halde migration tabloları üretmez.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
