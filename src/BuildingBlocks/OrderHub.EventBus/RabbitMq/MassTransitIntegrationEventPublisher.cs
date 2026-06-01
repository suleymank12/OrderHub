using MassTransit;

namespace OrderHub.EventBus.RabbitMq;

/// <summary>
/// <see cref="IIntegrationEventPublisher"/>'ın MassTransit implementasyonu. Yayımı <b>runtime (somut)</b>
/// tiple yapar: MassTransit exchange/routing'i mesajın CLR tipinden türetir; <see cref="IIntegrationEvent"/>
/// arayüzüyle yayım tüm olayları tek exchange'e toplardı (topology bozulur). <see cref="IPublishEndpoint"/>
/// scoped olduğundan bu publisher da scoped kaydedilir.
/// </summary>
internal sealed class MassTransitIntegrationEventPublisher(IPublishEndpoint publishEndpoint)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return publishEndpoint.Publish(integrationEvent, integrationEvent.GetType(), cancellationToken);
    }
}
