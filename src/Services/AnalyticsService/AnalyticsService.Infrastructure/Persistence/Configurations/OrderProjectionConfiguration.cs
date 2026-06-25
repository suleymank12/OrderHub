using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Infrastructure.Persistence.Converters;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="OrderProjection"/> read-model eşlemesi. PK = <see cref="OrderProjection.OrderId"/> (client-side,
/// upsert anahtarı). <see cref="OrderProjection.Status"/> string olarak saklanır (HasConversion). <c>*_at</c>
/// kolonları UTC-koruyucu converter ile. <see cref="OrderProjection.CustomerId"/> sorgu için index'li.
/// </summary>
internal sealed class OrderProjectionConfiguration : IEntityTypeConfiguration<OrderProjection>
{
    public void Configure(EntityTypeBuilder<OrderProjection> builder)
    {
        builder.ToTable("OrderProjections");

        builder.HasKey(projection => projection.OrderId);
        builder.Property(projection => projection.OrderId).ValueGeneratedNever();

        builder.Property(projection => projection.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(projection => projection.Total).HasColumnType("decimal(18,2)");
        builder.Property(projection => projection.Currency).IsRequired().HasMaxLength(3);

        builder.Property(projection => projection.CreatedAtUtc).HasConversion<UtcDateTimeConverter>();
        builder.Property(projection => projection.PaidAtUtc).HasConversion(new NullableUtcDateTimeConverter());
        builder.Property(projection => projection.LastUpdatedUtc).HasConversion<UtcDateTimeConverter>();

        builder.HasIndex(projection => projection.CustomerId)
            .HasDatabaseName("IX_OrderProjections_CustomerId");
    }
}
