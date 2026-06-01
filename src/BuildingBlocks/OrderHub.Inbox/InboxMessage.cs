namespace OrderHub.Inbox;

/// <summary>
/// Consumer-side dedup kaydı (Inbox pattern, ADR-0005). Bir <c>(MessageId, ConsumerType)</c> çiftinin
/// <b>commit edilmiş varlığı</b> = "bu mesaj bu consumer tarafından tam olarak işlendi" (Karar 4 — ayrı
/// ProcessedOnUtc ara durumu yok). <see cref="MessageId"/> = <c>IIntegrationEvent.Id</c> (Karar 2);
/// <see cref="ConsumerType"/> per-consumer dedup içindir (aynı mesaj farklı consumer'larca işlenebilir).
/// Kayıt, consumer'ın iş transaction'ı ile aynı SaveChanges'te atomik yazılır (Karar 3). Public setter yok.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>EF Core materialization için.</summary>
    private InboxMessage()
    {
    }

    private InboxMessage(Guid messageId, string consumerType, DateTime receivedOnUtc)
    {
        MessageId = messageId;
        ConsumerType = consumerType;
        ReceivedOnUtc = receivedOnUtc;
    }

    /// <summary>Mesaj kimliği = kaynak <c>IIntegrationEvent.Id</c> (uçtan uca dedup anahtarı). Composite PK #1.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>İşleyen consumer tipinin adı (per-consumer dedup). Composite PK #2.</summary>
    public string ConsumerType { get; private set; } = null!;

    /// <summary>Kaydın oluşturulma zamanı (UTC) — yalnızca audit/temizlik içindir (dedup sinyali değil).</summary>
    public DateTime ReceivedOnUtc { get; private set; }

    /// <summary>Yeni inbox kaydı. <paramref name="messageId"/> boş veya <paramref name="consumerType"/> boş → fail-fast.</summary>
    public static InboxMessage Create(Guid messageId, string consumerType)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Inbox message id cannot be empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(consumerType);

        return new InboxMessage(messageId, consumerType, DateTime.UtcNow);
    }
}
