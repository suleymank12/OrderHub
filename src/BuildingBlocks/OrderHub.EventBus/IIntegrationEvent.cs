namespace OrderHub.EventBus;

/// <summary>
/// Servis sınırını aşan bir entegrasyon olayını işaretler. Domain olayının (<c>IDomainEvent</c>,
/// OrderHub.Common) aksine bu sözleşme <b>servisler arası</b>dır ve mesaj bus'ına (RabbitMQ) yayımlanır.
/// İkisi bilinçli olarak ayrı tiplerdir (ADR-0002 "Faz 3 Evrim" Karar 3): domain olayı in-process kalır,
/// integration olayı dışarı çıkar — böylece servisler birbirinin domain modeline bağlanmaz.
/// <para>
/// <see cref="Id"/>, kaynak domain olayının <c>EventId</c>'sini taşır (ADR-0002 Karar 4): outbox PK +
/// unique index (producer-side dedup) ve consumer Inbox (consumer-side dedup) için <b>uçtan uca</b> dedup
/// anahtarıdır. <see cref="OccurredOnUtc"/> sıralama/audit içindir.
/// </para>
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Olayın benzersiz kimliği = kaynak domain olayının EventId'si (uçtan uca dedup anahtarı).</summary>
    Guid Id { get; }

    /// <summary>Olayın UTC oluşma zamanı.</summary>
    DateTime OccurredOnUtc { get; }
}
