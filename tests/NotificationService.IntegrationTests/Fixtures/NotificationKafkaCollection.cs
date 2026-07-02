namespace OrderHub.NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Consumer testi için iki-fixture'lı collection: SQL (projection DB) + Kafka (order-stream). Her iki
/// container da aynı test sınıfına enjekte edilir — gereksiz container spin-up'larını izole tutar.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NotificationKafkaCollection
    : ICollectionFixture<NotificationSqlServerContainerFixture>,
        ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "NotificationKafka";
}
