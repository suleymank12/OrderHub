namespace OrderHub.InventoryService.IntegrationTests.Fixtures;

/// <summary>
/// Consumer uçtan uca testleri için iki-fixture collection: SQL + RabbitMQ. PaymentEndToEndCollection
/// pattern'ini yansıtır. Yalnız hem DB hem broker gerektiren testlere hizmet eder; gereksiz
/// container spin-up izole tutulur.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InventoryEndToEndCollection
    : ICollectionFixture<InventorySqlServerContainerFixture>,
        ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "InventoryEndToEnd";
}
