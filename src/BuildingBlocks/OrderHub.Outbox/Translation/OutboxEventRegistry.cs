using OrderHub.Common.Primitives;
using OrderHub.EventBus;

namespace OrderHub.Outbox.Translation;

/// <summary>
/// Domain olay tipini integration olayına çeviren değişmez registry (ADR-0002 Faz 3 Karar 3). Eşleme
/// DI'dan beslenir (<see cref="OutboxEventRegistryBuilder"/>); interceptor hangi domain olayının hangi
/// integration olayına çevrileceğini hardcode etmez. Karşılığı olmayan domain olayı yalnız in-process kalır.
/// Yapıcı <c>internal</c>: yalnızca builder üretir → eşleme tek noktadan, doğrulanmış kurulur.
/// </summary>
public sealed class OutboxEventRegistry
{
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<Func<IDomainEvent, IIntegrationEvent>>> _factories;

    internal OutboxEventRegistry(
        IReadOnlyDictionary<Type, IReadOnlyList<Func<IDomainEvent, IIntegrationEvent>>> factories)
    {
        _factories = factories;
    }

    /// <summary>
    /// <paramref name="domainEvent"/> için kayıtlı çeviri(ler) varsa integration olaylarını <b>kayıt sırasıyla</b>
    /// üretir (1:1 → tek; 1:N fan-out → N, ADR-0006 Karar 4). Her biri için <c>integrationEvent.Id ==
    /// domainEvent.EventId</c> invariantını <b>runtime'da doğrular</b> (ADR-0002 Karar 4 — tüm fan-out hedefleri
    /// aynı EventId'yi taşır; eşleşmezse uçtan uca dedup zinciri kopardı → fail-fast). Karşılığı yoksa boş döner.
    /// </summary>
    public bool TryTranslate(IDomainEvent domainEvent, out IReadOnlyList<IIntegrationEvent> integrationEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!_factories.TryGetValue(domainEvent.GetType(), out var factories))
        {
            integrationEvents = [];
            return false;
        }

        var translated = new List<IIntegrationEvent>(factories.Count);
        foreach (var factory in factories)
        {
            var integrationEvent = factory(domainEvent);

            if (integrationEvent.Id != domainEvent.EventId)
            {
                throw new InvalidOperationException(
                    $"Outbox translation invariant violated for '{domainEvent.GetType().Name}': integration event " +
                    $"Id '{integrationEvent.Id}' must equal domain EventId '{domainEvent.EventId}' (end-to-end dedup).");
            }

            translated.Add(integrationEvent);
        }

        integrationEvents = translated;
        return true;
    }
}
