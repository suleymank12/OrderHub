namespace OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

/// <summary>
/// SQL gerektiren saga-persistence testleri için tek-fixture collection (container'ı testler arası paylaşır).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SagasDatabaseCollection
    : ICollectionFixture<SagasSqlServerContainerFixture>
{
    public const string Name = "SagasDatabase";
}
