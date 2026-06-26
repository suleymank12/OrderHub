namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Faz 4 producer testi için iki-fixture'lı collection: SQL (outbox) + Kafka (event-stream). RabbitMQ tarafı
/// capturing-stub ile in-process doğrulandığından gerçek RabbitMQ container'ı gerekmez (tek dış broker = Kafka).
/// </summary>
[CollectionDefinition(Name)]
public sealed class KafkaEndToEndCollection
    : ICollectionFixture<SqlServerContainerFixture>,
        ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "KafkaEndToEnd";
}
