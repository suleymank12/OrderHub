namespace OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL + gerçek RabbitMQ gerektiren saga uçtan uca testleri (Test B) için collection — her iki
/// container'ı testler arası paylaşır (container başlatma pahalı; assertion'lar hızlı bus start/stop ile izole).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SagaEndToEndCollection
    : ICollectionFixture<SagasSqlServerContainerFixture>,
      ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "SagaEndToEnd";
}
