using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Outbox processor'ın gördüğü tek <see cref="IIntegrationEventPublisher"/> (ADR-0006 Karar 3): event'i
/// marker'a göre doğru transport'a route eder — outbox/processor transport-agnostik kalır. <see cref="IKafkaEvent"/>
/// → Kafka; aksi (<see cref="IRabbitMqEvent"/> <b>veya işaretsiz</b>) → RabbitMQ. İşaretsiz'in RabbitMQ'ya
/// gitmesi Faz 3 davranışını korur (RabbitMQ tek transport'tu); Kafka yeni transport'a explicit opt-in'dir.
/// İki alt-publisher keyed DI ile enjekte edilir (<see cref="RabbitMqKey"/>/<see cref="KafkaKey"/>) → tip-belirsizliği yok.
/// </summary>
internal sealed class RoutingIntegrationEventPublisher(
    [FromKeyedServices(RoutingIntegrationEventPublisher.RabbitMqKey)] IIntegrationEventPublisher rabbitMqPublisher,
    [FromKeyedServices(RoutingIntegrationEventPublisher.KafkaKey)] IIntegrationEventPublisher kafkaPublisher)
    : IIntegrationEventPublisher
{
    internal const string RabbitMqKey = "eventbus:rabbitmq";
    internal const string KafkaKey = "eventbus:kafka";

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return integrationEvent is IKafkaEvent
            ? kafkaPublisher.PublishAsync(integrationEvent, cancellationToken)
            : rabbitMqPublisher.PublishAsync(integrationEvent, cancellationToken);
    }
}
