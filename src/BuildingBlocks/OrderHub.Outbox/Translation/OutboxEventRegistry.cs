using System.Diagnostics.CodeAnalysis;
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
    private readonly IReadOnlyDictionary<Type, Func<IDomainEvent, IIntegrationEvent>> _factories;

    internal OutboxEventRegistry(IReadOnlyDictionary<Type, Func<IDomainEvent, IIntegrationEvent>> factories)
    {
        _factories = factories;
    }

    /// <summary>
    /// <paramref name="domainEvent"/> için kayıtlı çeviri varsa integration olayını üretir. Üretim sonrası
    /// <c>integrationEvent.Id == domainEvent.EventId</c> invariantını <b>runtime'da doğrular</b> (ADR-0002
    /// Karar 4): eşleşmezse uçtan uca dedup zinciri kopardı → fail-fast.
    /// </summary>
    public bool TryTranslate(IDomainEvent domainEvent, [NotNullWhen(true)] out IIntegrationEvent? integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!_factories.TryGetValue(domainEvent.GetType(), out var factory))
        {
            integrationEvent = null;
            return false;
        }

        integrationEvent = factory(domainEvent);

        if (integrationEvent.Id != domainEvent.EventId)
        {
            throw new InvalidOperationException(
                $"Outbox translation invariant violated for '{domainEvent.GetType().Name}': integration event " +
                $"Id '{integrationEvent.Id}' must equal domain EventId '{domainEvent.EventId}' (end-to-end dedup).");
        }

        return true;
    }
}
