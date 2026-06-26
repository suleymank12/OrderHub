namespace OrderHub.EventBus;

/// <summary>
/// <b>Event-stream</b> integration event → Kafka (event-log, pub-sub, replay). ADR-0006 Karar 1/3/5:
/// "bu oldu" semantiği, çok bağımsız tüketici (Analytics, audit, …). <see cref="Topic"/> ve
/// <see cref="PartitionKey"/> publish-routing metadata'sıdır (event nereye/hangi partition'a gider).
/// Routing publisher bu marker'lı event'i Kafka'ya yönlendirir; aksi RabbitMQ'ya.
/// </summary>
public interface IKafkaEvent : IIntegrationEvent
{
    /// <summary>Hedef Kafka topic'i (örn. <c>order-hub.orders.events</c>).</summary>
    string Topic { get; }

    /// <summary>
    /// Partition key (örn. <c>OrderId</c>): aynı anahtarlı mesajlar aynı partition'a → per-key sıralama
    /// garantisi (ROADMAP §4.2). Boş/null olamaz.
    /// </summary>
    string PartitionKey { get; }
}
