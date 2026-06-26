namespace OrderHub.InventoryService.IntegrationTests.Fixtures;

/// <summary>
/// Yalnızca SQL gerektiren testler için tek-fixture collection (persistence testleri).
/// Broker gerektiren testler <see cref="InventoryEndToEndCollection"/> kullanır.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InventoryDatabaseCollection
    : ICollectionFixture<InventorySqlServerContainerFixture>
{
    public const string Name = "InventoryDatabase";
}
