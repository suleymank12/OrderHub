namespace OrderHub.PaymentService.IntegrationTests.Fixtures;

/// <summary>
/// Uçtan uca (gerçek broker) testler için iki-fixture'lı collection: SQL + RabbitMQ. Mevcut
/// <see cref="PaymentDatabaseCollection"/> (yalnız SQL) tek sorumluluk için ayrı kalır — bu collection
/// yalnızca hem DB hem broker isteyen 3c-4 testlerine hizmet eder (gereksiz container spin-up'ı izole tutar).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PaymentEndToEndCollection
    : ICollectionFixture<PaymentSqlServerContainerFixture>,
        ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "PaymentEndToEnd";
}
