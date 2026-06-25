using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderHub.Outbox.Persistence;

/// <summary>
/// <see cref="OutboxMessage"/>'ın EF Core eşlemesi. Servis <c>DbContext</c>'i bunu
/// <c>ApplyConfiguration</c> ile uygular (sonraki adım; bu building block migration üretmez). Tablo
/// <c>OutboxMessages</c>, PK <see cref="OutboxMessage.Id"/> (client-side = integration event Id, dedup).
/// İşlenmemiş kayıt sorgusu (<c>ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc</c>) için composite index;
/// filtered index yerine composite seçimi <b>provider-agnostik</b> kalır (SQL dialect'e bağlanmaz).
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever(); // Id client-side (= integration event Id).

        builder.Property(message => message.Type).IsRequired().HasMaxLength(500);
        builder.Property(message => message.Payload).IsRequired(); // nvarchar(max): payload boyutu sınırsız.
        builder.Property(message => message.OccurredOnUtc).IsRequired();
        builder.Property(message => message.ProcessedOnUtc);
        builder.Property(message => message.RetryCount).IsRequired();
        builder.Property(message => message.Error).HasMaxLength(2000);

        // Processor seek pattern'i: ProcessedOnUtc (NULL = işlenmemiş) → OccurredOnUtc (FIFO sıralama).
        // Leading kolon işlenmemişleri seçer, ikinci kolon sıralamayı karşılar → ek sort gerekmez.
        builder.HasIndex(message => new { message.ProcessedOnUtc, message.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc");
    }
}
