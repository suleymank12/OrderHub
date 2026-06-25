using OrderHub.Common.Primitives;
using OrderHub.EventBus;

namespace OrderHub.Outbox.Translation;

/// <summary>
/// <see cref="OutboxEventRegistry"/>'yi tip-güvenli kuran builder. Servis composition root'u (sonraki adım)
/// <c>AddOutbox(registry =&gt; registry.Map&lt;OrderConfirmed&gt;(e =&gt; new ProcessPayment... { Id = e.EventId, ... }))</c>
/// biçiminde besler. <c>dynamic</c>/<c>object</c> yok (§3): generic <see cref="Map{TDomainEvent}"/> derleme-zamanı
/// tip güvenliği sağlar, kapatma içinde tek cast yapılır.
/// </summary>
public sealed class OutboxEventRegistryBuilder
{
    private readonly Dictionary<Type, Func<IDomainEvent, IIntegrationEvent>> _factories = [];

    /// <summary>
    /// <typeparamref name="TDomainEvent"/> tipini bir integration olayına çeviren factory'yi kaydeder.
    /// Aynı domain tipi için ikinci kayıt = konfigürasyon hatası → fail-fast (sessiz override yok).
    /// </summary>
    public OutboxEventRegistryBuilder Map<TDomainEvent>(Func<TDomainEvent, IIntegrationEvent> factory)
        where TDomainEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (!_factories.TryAdd(typeof(TDomainEvent), domainEvent => factory((TDomainEvent)domainEvent)))
        {
            throw new InvalidOperationException(
                $"An outbox translation for domain event '{typeof(TDomainEvent).Name}' is already registered.");
        }

        return this;
    }

    /// <summary>Kayıtların değişmez bir kopyasıyla registry'yi üretir (builder sonradan değişse etkilenmez).</summary>
    internal OutboxEventRegistry Build() =>
        new(new Dictionary<Type, Func<IDomainEvent, IIntegrationEvent>>(_factories));
}
