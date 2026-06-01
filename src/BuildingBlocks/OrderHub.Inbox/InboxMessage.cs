namespace OrderHub.Inbox;

/// <summary>
/// Consumer-side dedup kaydı (Inbox pattern, ADR-0005). Bir <c>(MessageId, MessageType)</c> çiftinin
/// <b>commit edilmiş varlığı</b> = "bu mesaj bu tip için tam olarak işlendi" (Karar 4 — ayrı ProcessedOnUtc
/// ara durumu yok). <see cref="MessageId"/> = <c>IIntegrationEvent.Id</c> (Karar 2); <see cref="MessageType"/>
/// işlenen integration event tipinin adıdır (message-level filter discriminator'ı, Karar 6 — 1:1 topolojide
/// per-consumer'a eşdeğer). Kayıt, consumer'ın iş transaction'ı ile aynı SaveChanges'te atomik yazılır
/// (Karar 3). Public setter yok.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>EF Core materialization için.</summary>
    private InboxMessage()
    {
    }

    private InboxMessage(Guid messageId, string messageType, DateTime receivedOnUtc)
    {
        MessageId = messageId;
        MessageType = messageType;
        ReceivedOnUtc = receivedOnUtc;
    }

    /// <summary>Mesaj kimliği = kaynak <c>IIntegrationEvent.Id</c> (uçtan uca dedup anahtarı). Composite PK #1.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>İşlenen integration event tipinin adı (discriminator). Composite PK #2.</summary>
    public string MessageType { get; private set; } = null!;

    /// <summary>Kaydın oluşturulma zamanı (UTC) — yalnızca audit/temizlik içindir (dedup sinyali değil).</summary>
    public DateTime ReceivedOnUtc { get; private set; }

    /// <summary>Yeni inbox kaydı. <paramref name="messageId"/> boş veya <paramref name="messageType"/> boş → fail-fast.</summary>
    public static InboxMessage Create(Guid messageId, string messageType)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Inbox message id cannot be empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        return new InboxMessage(messageId, messageType, DateTime.UtcNow);
    }
}
