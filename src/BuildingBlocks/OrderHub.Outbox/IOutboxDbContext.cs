using Microsoft.EntityFrameworkCore;

namespace OrderHub.Outbox;

/// <summary>
/// Outbox processor'ın ihtiyaç duyduğu minimal kalıcılık port'u. Servisin <c>DbContext</c>'i bunu
/// implement eder (database-per-service: outbox tablosu domain ile <b>aynı</b> DB'dedir → tek transaction,
/// ADR-0002 Faz 3 Karar 1). Processor concrete <c>DbContext</c>'e değil bu arayüze bağlanır (DIP) →
/// building block servise sızmaz.
/// </summary>
public interface IOutboxDbContext
{
    /// <summary>İşlenmemiş ve işlenmiş outbox mesajları.</summary>
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>Processor'ın ProcessedOnUtc/RetryCount güncellemelerini kalıcılaştırır.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
