using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Infrastructure.Persistence.Converters;

namespace OrderHub.InventoryService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Reservation"/> entity'sinin EF Core eşlemesi (<see cref="StockItem"/> aggregate'inin parçası;
/// shadow <c>StockItemId</c> FK StockItem konfigürasyonunda tanımlı). Optimistic concurrency aggregate KÖKÜNDE
/// (StockItem.RowVersion) — entity'de ayrı token yok. <c>OrderId</c> index'i saga "siparişin rezervasyonu" sorgusu.
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever(); // Id Reservation.Create üretir.

        builder.Property(reservation => reservation.OrderId).IsRequired();
        builder.Property(reservation => reservation.Quantity).IsRequired();
        builder.Property(reservation => reservation.Status).IsRequired();

        builder.Property(reservation => reservation.CreatedAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();
        builder.Property(reservation => reservation.ExpiresAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();

        // Saga sorgu pattern'i: bir siparişin rezervasyonunu bulma (confirm/release hedefi).
        builder.HasIndex(reservation => reservation.OrderId);
    }
}
