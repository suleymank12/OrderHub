namespace OrderHub.Common.Primitives;

/// <summary>
/// Bir domain olayını işaretler. <b>Bilerek MediatR'ın <c>INotification</c>'ını inherit
/// ETMEZ</b>: Domain katmanı hiçbir altyapıya bağlanmaz (Clean Architecture); MediatR'a
/// adaptasyon Infrastructure'daki dispatcher'da yapılır. <see cref="EventId"/> outbox
/// idempotency'si, <see cref="OccurredOnUtc"/> sıralama/audit içindir.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Olayın benzersiz kimliği (idempotent işleme / dedup için).</summary>
    Guid EventId { get; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    DateTime OccurredOnUtc { get; }
}
