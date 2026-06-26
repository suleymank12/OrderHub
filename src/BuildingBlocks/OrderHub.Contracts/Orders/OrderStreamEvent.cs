using System.Text.Json.Serialization;
using OrderHub.EventBus;

namespace OrderHub.Contracts.Orders;

/// <summary>
/// OrderService sipariş yaşam-döngüsü olaylarının Kafka event-stream taban tipi (ADR-0006). Hepsi
/// <see cref="OrdersTopic"/>'e, key = <see cref="PartitionKey"/> (OrderId) ile gider → per-order ordering
/// (ROADMAP §4.2). AnalyticsService (4c) bu tipleri tüketip CQRS read-model üretir → producer/consumer aynı
/// CLR tipini paylaşır (Contracts). <see cref="Id"/> kaynak domain EventId'sini taşır (uçtan uca dedup,
/// ADR-0002 Karar 4). <c>Money</c>/domain tipi sızdırılmaz → primitive alanlar. <see cref="Topic"/> ve
/// <see cref="PartitionKey"/> publish-routing metadata'sıdır → JSON value'ya yazılmaz (<see cref="JsonIgnoreAttribute"/>).
/// </summary>
public abstract record OrderStreamEvent : IKafkaEvent
{
    /// <summary>Tüm sipariş olaylarının gittiği tek Kafka topic'i (ROADMAP §4.2).</summary>
    public const string OrdersTopic = "order-hub.orders.events";

    /// <summary>Olay kimliği = kaynak domain EventId (uçtan uca dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>İlgili siparişin kimliği (partition key kaynağı).</summary>
    public required Guid OrderId { get; init; }

    [JsonIgnore]
    public string Topic => OrdersTopic;

    [JsonIgnore]
    public string PartitionKey => OrderId.ToString();
}
