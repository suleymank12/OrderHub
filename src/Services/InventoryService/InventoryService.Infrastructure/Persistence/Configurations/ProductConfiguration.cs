using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.InventoryService.Domain.Catalog;
using OrderHub.InventoryService.Infrastructure.Persistence.Converters;

namespace OrderHub.InventoryService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Product"/> katalog aggregate'inin EF Core eşlemesi (düz tablo). Stok kolonu YOK — stok
/// <see cref="OrderHub.InventoryService.Domain.Stock.StockItem"/>'da (ayrı aggregate).
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever(); // Id client-side (Product.Create).

        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.Sku).HasMaxLength(64).IsUnicode(false);

        builder.Property(product => product.CreatedAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();

        // Domain event'ler kolon değil; EF map etmeye çalışmasın.
        builder.Ignore(product => product.DomainEvents);
    }
}
