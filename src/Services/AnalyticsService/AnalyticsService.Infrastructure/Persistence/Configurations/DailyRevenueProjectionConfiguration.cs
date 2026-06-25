using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.AnalyticsService.Domain.Revenue;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="DailyRevenueProjection"/> read-model eşlemesi. PK = <see cref="DailyRevenueProjection.Date"/>
/// (gün; <see cref="DateOnly"/> → SQL <c>date</c>). Parasal kolonlar <c>decimal(18,2)</c>.
/// </summary>
internal sealed class DailyRevenueProjectionConfiguration : IEntityTypeConfiguration<DailyRevenueProjection>
{
    public void Configure(EntityTypeBuilder<DailyRevenueProjection> builder)
    {
        builder.ToTable("DailyRevenueProjections");

        builder.HasKey(projection => projection.Date);
        builder.Property(projection => projection.Date).ValueGeneratedNever();

        builder.Property(projection => projection.TotalOrders);
        builder.Property(projection => projection.TotalRevenue).HasColumnType("decimal(18,2)");
        builder.Property(projection => projection.AvgOrderValue).HasColumnType("decimal(18,2)");
    }
}
