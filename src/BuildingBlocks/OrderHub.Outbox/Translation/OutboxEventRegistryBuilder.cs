using OrderHub.Common.Primitives;
using OrderHub.EventBus;

namespace OrderHub.Outbox.Translation;

/// <summary>
/// <see cref="OutboxEventRegistry"/>'yi tip-güvenli kuran builder. Servis composition root'u
/// <c>AddOutbox(registry =&gt; registry.Map&lt;OrderConfirmed&gt;(e =&gt; new ProcessPayment... { Id = e.EventId, ... }))</c>
/// biçiminde besler. <c>dynamic</c>/<c>object</c> yok (§3): generic <see cref="Map{TDomainEvent}"/> derleme-zamanı
/// tip güvenliği sağlar, kapatma içinde tek cast yapılır.
/// <para>
/// <b>1:N fan-out (ADR-0006 Karar 4):</b> aynı domain tipi için <see cref="Map{TDomainEvent}"/> birden çok kez
/// çağrılabilir (örn. OrderConfirmed → RabbitMQ ProcessPayment + Kafka OrderConfirmed). Her çağrı bir hedef
/// ekler; <b>kayıt sırası = fan-out sırası</b> (interceptor bunu deterministik <c>Ordinal</c> 0,1,… olarak
/// yazar). Tek çağrı = tek hedef (1:1, geriye uyumlu). Açık <c>Map</c>-zinciri "magic" olmadan niyeti gösterir.
/// </para>
/// </summary>
public sealed class OutboxEventRegistryBuilder
{
    private readonly Dictionary<Type, List<Func<IDomainEvent, IIntegrationEvent>>> _factories = [];

    /// <summary>
    /// <typeparamref name="TDomainEvent"/> tipini bir integration olayına çeviren factory'yi kaydeder (sıra korunur).
    /// Aynı tip için tekrar çağrı = ek fan-out hedefi (1:N); fail-fast YOK — kasıtlı çoklu-hedef desteklenir.
    /// </summary>
    public OutboxEventRegistryBuilder Map<TDomainEvent>(Func<TDomainEvent, IIntegrationEvent> factory)
        where TDomainEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (!_factories.TryGetValue(typeof(TDomainEvent), out var factories))
        {
            factories = [];
            _factories[typeof(TDomainEvent)] = factories;
        }

        factories.Add(domainEvent => factory((TDomainEvent)domainEvent));
        return this;
    }

    /// <summary>Kayıtların değişmez bir kopyasıyla registry'yi üretir (builder sonradan değişse etkilenmez).</summary>
    internal OutboxEventRegistry Build() =>
        new(_factories.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Func<IDomainEvent, IIntegrationEvent>>)pair.Value.ToList()));
}
