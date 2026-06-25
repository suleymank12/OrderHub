using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderHub.Inbox.Persistence;

/// <summary>
/// <see cref="InboxMessage"/>'ın EF Core eşlemesi. Servis <c>DbContext</c>'i bunu explicit
/// <c>ApplyConfiguration</c> ile uygular (farklı assembly → assembly-scan yakalamaz). <b>Composite PK
/// (MessageId, MessageType)</b>: doğal kimlik; hem benzersizliği (concurrency backstop, ADR-0005 Karar 5)
/// hem varlık-sorgusu seek'ini tek yapıyla karşılar → surrogate kolon yok.
/// </summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        // Composite PK = dedup kimliği + unique constraint (ayrı index gerekmez).
        builder.HasKey(message => new { message.MessageId, message.MessageType });

        // PK string kolonu bounded olmalı (nvarchar(max) PK olamaz). Tip FullName'leri için 256 yeterli.
        builder.Property(message => message.MessageType).HasMaxLength(256);

        builder.Property(message => message.ReceivedOnUtc).IsRequired();
    }
}
