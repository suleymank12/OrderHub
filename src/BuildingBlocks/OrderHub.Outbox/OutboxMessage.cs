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

    private OutboxMessage(Guid id, int ordinal, string type, string payload, DateTime occurredOnUtc)
    {
        Id = id;
        Ordinal = ordinal;
        Type = type;
        Payload = payload;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>Mesaj kimliği = integration event Id = domain EventId (composite PK #1, dedup zinciri — ADR-0002 Karar 4).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Aynı domain olayının fan-out sırası (composite PK #2, ADR-0006 Karar 4): tek-hedef = 0; çok-hedef
    /// (RabbitMQ + Kafka) = 0,1,… <see cref="Id"/> sabit (== EventId) kalırken N satırın PK çakışmasını engeller.
    /// Yalnız outbox-içi disambiguator — consumer'a ulaşmaz (inbox dedup'ı hâlâ (Id, MessageType)).
    /// </summary>
    public int Ordinal { get; private set; }

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

    /// <summary>
    /// Yeni bir outbox mesajı oluşturur. <paramref name="id"/> boş, tip/payload boş veya <paramref name="ordinal"/>
    /// negatif → fail-fast. <paramref name="ordinal"/> varsayılan 0 (tek-hedef); fan-out'ta interceptor 0,1,… verir.
    /// </summary>
    public static OutboxMessage Create(Guid id, string type, string payload, DateTime occurredOnUtc, int ordinal = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message id cannot be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage(id, ordinal, type, payload, occurredOnUtc);
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
