using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="OrderItem"/> entity'sinin EF Core eşlemesi. Kendi kimliği olan bir entity (Order'ın
/// aggregate boundary'si içinde); <c>UnitPrice</c> owned Money kolonlarına açılır. <c>Subtotal</c>
/// computed (UnitPrice × Quantity) olduğu için kolon değildir, ignore edilir.
/// </summary>
internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever(); // Id client-side (OrderItem.Create).

        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();

        // UnitPrice (Money) → owned kolonlar; Order.Total ile aynı para semantiği (decimal(18,2) + currency string).
        builder.OwnsOne(item => item.UnitPrice, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("UnitPriceAmount")
                .HasColumnType("decimal(18,2)");
            money.Property(m => m.Currency)
                .HasColumnName("UnitPriceCurrency")
                .HasConversion<string>()
                .HasMaxLength(3)
                .IsUnicode(false);
        });
        builder.Navigation(item => item.UnitPrice).IsRequired();

        // Subtotal computed property → kolon değil.
        builder.Ignore(item => item.Subtotal);
    }
}
