using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Infrastructure.Persistence.Converters;

namespace OrderHub.OrderService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Order"/> aggregate root'unun EF Core eşlemesi. Owned type'lar (Total, ShippingAddress)
/// aynı tabloda kolonlara açılır; kalemler ayrı <c>OrderItems</c> tablosunda one-to-many (cascade).
/// Optimistic concurrency shadow <c>RowVersion</c> ile (domain'e dokunmadan).
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever(); // Id client-side üretilir (Order.Create).

        builder.Property(order => order.CustomerId).IsRequired();

        // enum int olarak; durum sorgu/filtreleri için yeterli (Currency'den farklı: status iç kullanım).
        builder.Property(order => order.Status).IsRequired();

        // UTC kind koruması (datetime2 Kind tutmaz → sessiz timezone bug engellenir).
        builder.Property(order => order.CreatedAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();
        builder.Property(order => order.ConfirmedAtUtc).HasConversion<NullableUtcDateTimeConverter>();
        builder.Property(order => order.CancelledAtUtc).HasConversion<NullableUtcDateTimeConverter>();

        builder.Property(order => order.CancellationReason).HasMaxLength(500);

        // Total (Money) → aynı tabloda owned kolonlar; her zaman var (required).
        builder.OwnsOne(order => order.Total, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("TotalAmount")
                .HasColumnType("decimal(18,2)"); // settled money → 2 ondalık (TRY/USD/EUR standardı).
            money.Property(m => m.Currency)
                .HasColumnName("TotalCurrency")
                .HasConversion<string>() // human-readable DB + API/query stability.
                .HasMaxLength(3) // ISO 4217 kodları (TRY/USD/EUR) 3 char.
                .IsUnicode(false);
        });
        builder.Navigation(order => order.Total).IsRequired();

        // ShippingAddress (Address) → owned kolonlar; required. MaxLength → şema disiplini.
        builder.OwnsOne(order => order.ShippingAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("ShippingStreet").HasMaxLength(200).IsRequired();
            address.Property(a => a.City).HasColumnName("ShippingCity").HasMaxLength(100).IsRequired();
            address.Property(a => a.PostalCode).HasColumnName("ShippingPostalCode").HasMaxLength(20).IsRequired();
            address.Property(a => a.Country).HasColumnName("ShippingCountry").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(order => order.ShippingAddress).IsRequired();

        // Items salt-okunur (_items.AsReadOnly()) → EF backing field'a yazmalı; OrderItem'de geri-navigation yok.
        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey("OrderId") // shadow FK; aggregate boundary tek girişli.
            .IsRequired() // OrderItem Order'sız var olamaz → FK non-nullable, orphan DB'de imkânsız.
            .OnDelete(DeleteBehavior.Cascade); // Order silinince kalemleri de silinir (aggregate boundary).
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Optimistic concurrency: shadow token (domain'e dokunmadan). Faz 3 concurrent update'lerini korur.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        // Query pattern'leri: müşteri siparişleri ve zaman bazlı sayfalama/sıralama.
        builder.HasIndex(order => order.CustomerId);
        builder.HasIndex(order => order.CreatedAtUtc);

        // Domain event'ler kolon değil; EF map etmeye çalışmasın.
        builder.Ignore(order => order.DomainEvents);
    }
}
