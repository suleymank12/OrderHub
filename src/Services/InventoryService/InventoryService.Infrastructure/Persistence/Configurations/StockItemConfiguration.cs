using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Infrastructure.Persistence.Converters;

namespace OrderHub.InventoryService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="StockItem"/> aggregate root'unun EF Core eşlemesi. Rezervasyonlar salt-okunur backing field
/// (<c>_reservations</c>) → one-to-many (cascade); <c>Reservations</c> ayrı tabloda. <b>Optimistic concurrency:</b>
/// shadow <c>RowVersion</c> (ADR-0007 Karar 3) — saga eşzamanlı reserve/confirm/release contention'ında ikinci
/// yazma reddedilir → retry → güncel state görülür (domain'e dokunmadan, OrderConfiguration precedent'i).
/// </summary>
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems");

        builder.HasKey(stockItem => stockItem.Id);
        builder.Property(stockItem => stockItem.Id).ValueGeneratedNever(); // Id client-side (StockItem.Create).

        builder.Property(stockItem => stockItem.ProductId).IsRequired();
        builder.Property(stockItem => stockItem.AvailableQuantity).IsRequired();

        builder.Property(stockItem => stockItem.CreatedAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();

        // Reservations salt-okunur (_reservations.AsReadOnly()) → EF backing field'a yazar; Reservation'da
        // geri-navigation yok (aggregate tek girişli). Cascade: StockItem silinince rezervasyonları da silinir.
        builder.HasMany(stockItem => stockItem.Reservations)
            .WithOne()
            .HasForeignKey("StockItemId") // shadow FK.
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(stockItem => stockItem.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Optimistic concurrency: shadow token (ADR-0007 Karar 3, domain'e dokunmadan).
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        // Sorgu pattern'i: bir ürünün stok kalemini bulma (saga reserve hedefi).
        builder.HasIndex(stockItem => stockItem.ProductId);

        // Domain event'ler kolon değil; EF map etmeye çalışmasın.
        builder.Ignore(stockItem => stockItem.DomainEvents);
    }
}
