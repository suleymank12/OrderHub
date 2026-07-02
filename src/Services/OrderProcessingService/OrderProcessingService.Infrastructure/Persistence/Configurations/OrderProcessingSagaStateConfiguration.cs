using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.OrderProcessingService.Infrastructure.Persistence.Converters;
using OrderHub.OrderProcessingService.Infrastructure.Saga;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="OrderProcessingSagaState"/>'in EF Core eşlemesi (saga repository tablosu, <c>OrderHub_Sagas</c>).
/// <b>PK = CorrelationId</b> (= OrderId, client-side üretilir → <c>ValueGeneratedNever</c>). <b>Optimistic
/// concurrency:</b> <see cref="OrderProcessingSagaState.RowVersion"/> SQL Server <c>rowversion</c> token'ı
/// (ADR-0007 Karar 3) — eşzamanlı StockReserved/Confirmed yazmaları biri reddedilir → MassTransit retry → taze
/// state. Fan-out kümeleri (<c>AllProductIds</c>/<c>ReservedProductIds</c>/<c>ConfirmedProductIds</c>) tek JSON
/// kolonda (<see cref="GuidSetJsonConverter"/> + <see cref="GuidSetValueComparer"/>, Karar B).
/// </summary>
internal sealed class OrderProcessingSagaStateConfiguration : IEntityTypeConfiguration<OrderProcessingSagaState>
{
    public void Configure(EntityTypeBuilder<OrderProcessingSagaState> builder)
    {
        builder.ToTable("OrderProcessingSagaStates");

        builder.HasKey(state => state.CorrelationId);
        builder.Property(state => state.CorrelationId).ValueGeneratedNever(); // = OrderId (client-side).

        builder.Property(state => state.CurrentState).IsRequired().HasMaxLength(64);

        // Optimistic concurrency token (SQL Server rowversion). MassTransit EF saga repo Optimistic mode bunu kullanır.
        builder.Property(state => state.RowVersion).IsRowVersion();

        builder.Property(state => state.CustomerId).IsRequired();
        builder.Property(state => state.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(state => state.Currency).HasMaxLength(3).IsRequired();
        builder.Property(state => state.ItemCount).IsRequired();

        // Fan-out kümeleri (Karar B) → JSON kolon (converter) + ValueComparer (mutasyon tespiti, snapshot derin kopya).
        var converter = new GuidSetJsonConverter();
        var comparer = new GuidSetValueComparer();

        builder.Property(state => state.AllProductIds).HasConversion(converter, comparer).IsRequired();
        builder.Property(state => state.ReservedProductIds).HasConversion(converter, comparer).IsRequired();
        builder.Property(state => state.ConfirmedProductIds).HasConversion(converter, comparer).IsRequired();

        // Compensation (5e-2): hedef release seti (dondurulmuş) + serbest bırakılanlar → aynı JSON küme deseni.
        builder.Property(state => state.ProductsToRelease).HasConversion(converter, comparer).IsRequired();
        builder.Property(state => state.ReleasedProductIds).HasConversion(converter, comparer).IsRequired();

        // İptal gerekçesi yalnız telafi dalında dolar → nullable (happy-path'te null).
        builder.Property(state => state.CancellationReason).HasMaxLength(64);
    }
}
