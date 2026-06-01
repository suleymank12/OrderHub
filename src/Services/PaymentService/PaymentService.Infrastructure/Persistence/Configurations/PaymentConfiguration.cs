using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderHub.PaymentService.Domain.Payments;
using OrderHub.PaymentService.Infrastructure.Persistence.Converters;

namespace OrderHub.PaymentService.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Payment"/> aggregate root'unun EF Core eşlemesi. Düz tablo (owned type yok — tutar primitive
/// Amount/Currency). Optimistic concurrency shadow <c>RowVersion</c> ile (domain'e dokunmadan).
/// </summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).ValueGeneratedNever(); // Id client-side (Payment.Create).

        builder.Property(payment => payment.OrderId).IsRequired();

        builder.Property(payment => payment.Amount)
            .HasColumnType("decimal(18,2)") // settled money → 2 ondalık.
            .IsRequired();

        builder.Property(payment => payment.Currency)
            .HasMaxLength(3) // ISO 4217 kodları 3 char.
            .IsUnicode(false)
            .IsRequired();

        builder.Property(payment => payment.Status).IsRequired();

        builder.Property(payment => payment.ExternalTransactionId).HasMaxLength(100);

        // UTC kind koruması (datetime2 Kind tutmaz → sessiz timezone bug engellenir).
        builder.Property(payment => payment.CreatedAtUtc)
            .HasConversion<UtcDateTimeConverter>()
            .IsRequired();

        // Optimistic concurrency: shadow token (domain'e dokunmadan).
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        // Sorgu pattern'i: bir siparişin ödemelerini bulma (Faz 5 saga / idempotency).
        builder.HasIndex(payment => payment.OrderId);

        // Domain event'ler kolon değil; EF map etmeye çalışmasın.
        builder.Ignore(payment => payment.DomainEvents);
    }
}
