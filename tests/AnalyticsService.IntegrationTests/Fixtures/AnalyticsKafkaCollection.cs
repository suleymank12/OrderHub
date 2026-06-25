namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>
/// Consumer testi için iki-fixture'lı collection: SQL (projection DB) + Kafka (order-stream). 4c-1
/// <see cref="AnalyticsDatabaseCollection"/> (yalnız SQL) dokunulmadan kalır — bu collection hem DB hem broker
/// isteyen 4c-2 testine hizmet eder (gereksiz container spin-up'ı izole tutar).
/// </summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsKafkaCollection
    : ICollectionFixture<AnalyticsSqlServerContainerFixture>,
        ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "AnalyticsKafka";
}
