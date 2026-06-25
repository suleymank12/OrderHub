using Moq;
using OrderHub.EventBus.Kafka;

namespace OrderHub.EventBus.UnitTests.Kafka;

/// <summary>
/// <see cref="RoutingIntegrationEventPublisher"/> marker-routing (ADR-0006 Karar 3): <see cref="IKafkaEvent"/>
/// → Kafka publisher; <see cref="IRabbitMqEvent"/> ve <b>işaretsiz</b> → RabbitMQ publisher (Faz 3 default korunur).
/// İki alt-publisher mock'lanır; doğru olana dispatch + diğerinin HİÇ çağrılmaması doğrulanır.
/// </summary>
public sealed class RoutingIntegrationEventPublisherTests
{
    private readonly Mock<IIntegrationEventPublisher> _rabbitMq = new();
    private readonly Mock<IIntegrationEventPublisher> _kafka = new();
    private readonly RoutingIntegrationEventPublisher _sut;

    public RoutingIntegrationEventPublisherTests()
    {
        _rabbitMq.Setup(p => p.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _kafka.Setup(p => p.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sut = new RoutingIntegrationEventPublisher(_rabbitMq.Object, _kafka.Object);
    }

    [Fact]
    public async Task PublishAsync_KafkaEvent_RoutesToKafkaOnly()
    {
        var evt = new KafkaTestEvent { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow };

        await _sut.PublishAsync(evt);

        _kafka.Verify(p => p.PublishAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        _rabbitMq.Verify(p => p.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_RabbitMqEvent_RoutesToRabbitMqOnly()
    {
        var evt = new RabbitMqTestEvent { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow };

        await _sut.PublishAsync(evt);

        _rabbitMq.Verify(p => p.PublishAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        _kafka.Verify(p => p.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_UnmarkedEvent_DefaultsToRabbitMq()
    {
        // Faz 3 davranışı: marker'sız integration event RabbitMQ'ya gider (geriye uyum, ADR-0006 Karar 3).
        var evt = new UnmarkedTestEvent { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow };

        await _sut.PublishAsync(evt);

        _rabbitMq.Verify(p => p.PublishAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        _kafka.Verify(p => p.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed record KafkaTestEvent : IKafkaEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
        public string Topic => "order-hub.orders.events";
        public string PartitionKey => Id.ToString();
    }

    private sealed record RabbitMqTestEvent : IRabbitMqEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
    }

    private sealed record UnmarkedTestEvent : IIntegrationEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
    }
}
