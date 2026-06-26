using System.Text;
using Confluent.Kafka;
using Moq;
using OrderHub.EventBus.Kafka;

namespace OrderHub.EventBus.UnitTests.Kafka;

/// <summary>
/// <see cref="KafkaIntegrationEventPublisher"/>: <see cref="IKafkaEvent"/>'i topic'e, key=PartitionKey, value=JSON
/// ile produce eder (ADR-0006). <see cref="IProducer{TKey,TValue}"/> mock'lanır; produce argümanları + idempotent
/// producer config doğrulanır. Non-Kafka event → fail-fast.
/// </summary>
public sealed class KafkaIntegrationEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_KafkaEvent_ProducesToTopic_WithPartitionKeyAndJsonValue()
    {
        var producer = new Mock<IProducer<string, string>>();
        string? capturedTopic = null;
        Message<string, string>? capturedMessage = null;
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>(
                (topic, message, _) => (capturedTopic, capturedMessage) = (topic, message))
            .ReturnsAsync(new DeliveryResult<string, string>());

        var orderId = Guid.NewGuid();
        var integrationEvent = new OrderEventStub { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId };

        await new KafkaIntegrationEventPublisher(producer.Object).PublishAsync(integrationEvent);

        capturedTopic.Should().Be("order-hub.orders.events");
        capturedMessage.Should().NotBeNull();
        capturedMessage!.Key.Should().Be(orderId.ToString(), "partition key = OrderId (§4.2 ordering)");
        capturedMessage.Value.Should().Contain(orderId.ToString(), "value = somut event JSON'u");

        // Tip header'da → consumer dispatch anahtarı (value JSON tip taşımaz).
        var typeHeader = Encoding.UTF8.GetString(capturedMessage.Headers.GetLastBytes(KafkaMessageHeaders.MessageType));
        typeHeader.Should().Be(typeof(OrderEventStub).FullName);
    }

    [Fact]
    public async Task PublishAsync_NonKafkaEvent_ThrowsInvalidOperation()
    {
        var producer = new Mock<IProducer<string, string>>();

        var act = () => new KafkaIntegrationEventPublisher(producer.Object)
            .PublishAsync(new NonKafkaStub { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void BuildProducerConfig_IsIdempotent_AndAcksAll()
    {
        // ADR-0006 Karar 2 / ROADMAP §4.3: idempotent producer.
        var config = KafkaEventBusServiceCollectionExtensions.BuildProducerConfig("broker:9092");

        config.BootstrapServers.Should().Be("broker:9092");
        config.Acks.Should().Be(Acks.All);
        config.EnableIdempotence.Should().BeTrue();
    }

    private sealed record OrderEventStub : IKafkaEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
        public Guid OrderId { get; init; }
        public string Topic => "order-hub.orders.events";
        public string PartitionKey => OrderId.ToString();
    }

    private sealed record NonKafkaStub : IIntegrationEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
    }
}
