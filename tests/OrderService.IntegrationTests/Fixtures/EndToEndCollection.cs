namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Uçtan uca (gerçek broker) testler için iki-fixture'lı collection: SQL + RabbitMQ. Mevcut
/// <see cref="DatabaseCollection"/> (yalnız SQL) ve <see cref="MessagingCollection"/> (yalnız RabbitMQ)
/// dokunulmadan kalır — bu collection yalnızca hem DB hem broker isteyen 3c-4 testlerine hizmet eder.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EndToEndCollection
    : ICollectionFixture<SqlServerContainerFixture>,
        ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "EndToEnd";
}
