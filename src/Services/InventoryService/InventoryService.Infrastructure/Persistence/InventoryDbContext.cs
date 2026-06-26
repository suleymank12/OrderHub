using Microsoft.EntityFrameworkCore;
using OrderHub.InventoryService.Domain.Catalog;
using OrderHub.InventoryService.Domain.Stock;

namespace OrderHub.InventoryService.Infrastructure.Persistence;

/// <summary>
/// InventoryService'in EF Core <see cref="DbContext"/>'i. Kendi DB'si: <c>OrderHub_Inventory</c>
/// (database-per-service). 5b iskeleti: yalnız domain şeması (<see cref="Product"/> katalog +
/// <see cref="StockItem"/> aggregate'i; <c>Reservation</c> StockItem navigation'ı üzerinden eşlenir, ayrı DbSet
/// yok — aggregate sınırı). <b>Outbox/Inbox + IUnitOfWork 5c'de</b> (mesajlaşma geldiğinde) eklenecek; şimdi
/// kullanılmayan altyapı taşınmaz (dead-code kaçınma, AnalyticsService 4c-1 dersi).
/// </summary>
internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockItem> StockItems => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (Product/StockItem/Reservation) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
