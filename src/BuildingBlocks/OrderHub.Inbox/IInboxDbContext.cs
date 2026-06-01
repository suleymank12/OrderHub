using Microsoft.EntityFrameworkCore;

namespace OrderHub.Inbox;

/// <summary>
/// Inbox filter'ın ihtiyaç duyduğu minimal kalıcılık port'u. Servisin <c>DbContext</c>'i bunu implement eder
/// (database-per-service: inbox tablosu domain ile <b>aynı</b> DB'dedir → consumer'ın iş transaction'ıyla
/// atomik, ADR-0005 Karar 3). Filter concrete <c>DbContext</c>'e değil bu arayüze bağlanır (DIP).
/// </summary>
public interface IInboxDbContext
{
    /// <summary>İşlenmiş (MessageId, ConsumerType) kayıtları.</summary>
    DbSet<InboxMessage> InboxMessages { get; }

    /// <summary>Consumer'ın iş state'i ile birlikte (tek SaveChanges) inbox kaydını da kalıcılaştırır.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
