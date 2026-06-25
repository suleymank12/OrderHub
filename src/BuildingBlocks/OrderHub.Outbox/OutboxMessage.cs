namespace OrderHub.Outbox;

/// <summary>
/// Outbox tablosundaki tek bir kalıcı entegrasyon mesajı. Domain state ile <b>aynı transaction</b>'da
/// (pre-commit interceptor) yazılır → dual-write penceresi kapanır (ADR-0002 Faz 3 Karar 1). Serileştirme
/// bilinçli olarak <b>dışarıda</b> (interceptor/serializer) yapılır; entity yalnızca veri + durum geçişi
/// invariantlarını tutar (SRP). <see cref="Id"/> = kaynak <c>IIntegrationEvent.Id</c> = domain EventId
/// (PK + unique index → producer-side dedup, Karar 4).
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>EF Core materialization için.</summary>
    private OutboxMessage()
    {
    }

    private OutboxMessage(Guid id, string type, string payload, DateTime occurredOnUtc)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>Mesaj kimliği = integration event Id = domain EventId (PK, unique → dedup).</summary>
    public Guid Id { get; private set; }

    /// <summary>Serileştirilmiş olayın CLR tipi (AssemblyQualifiedName) — publish öncesi geri-çözümleme için.</summary>
    public string Type { get; private set; } = null!;

    /// <summary>Olayın JSON gövdesi (System.Text.Json).</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Kaynak olayın UTC oluşma zamanı — processor sıralaması (FIFO) bunun üzerinden.</summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Başarılı publish zamanı (UTC); işlenmemişse null.</summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>Başarısız publish deneme sayısı; <c>MaxRetryCount</c>'a ulaşınca DLQ/manual (artık çekilmez).</summary>
    public int RetryCount { get; private set; }

    /// <summary>Son hatanın mesajı (varsa); başarılı publish'te temizlenir.</summary>
    public string? Error { get; private set; }

    /// <summary>Yeni bir outbox mesajı oluşturur. <paramref name="id"/> boş veya tip/payload boş → fail-fast.</summary>
    public static OutboxMessage Create(Guid id, string type, string payload, DateTime occurredOnUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message id cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage(id, type, payload, occurredOnUtc);
    }

    /// <summary>Mesajı başarılı işlenmiş olarak işaretler (idempotent: tekrar çekilmez).</summary>
    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
    }

    /// <summary>Başarısız denemeyi kaydeder: retry sayacını artırır ve son hatayı tutar.</summary>
    public void MarkFailed(string error)
    {
        RetryCount++;
        Error = error;
    }
}
