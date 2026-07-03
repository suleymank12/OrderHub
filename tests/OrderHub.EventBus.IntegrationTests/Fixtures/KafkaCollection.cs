namespace OrderHub.EventBus.IntegrationTests.Fixtures;

/// <summary>Gerçek Kafka container fixture'ını paylaşan collection (6c-2 trace propagation e2e).</summary>
[CollectionDefinition(Name)]
public sealed class KafkaCollection : ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "Kafka";
}
