namespace OrderHub.NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Cart-abandonment e2e collection (5f-3): tek <see cref="NotificationAppFixture"/>'ı (tam Program + SQL + Kafka +
/// Hangfire) testler arası paylaşır. 5f-1 <c>NotificationKafka</c> collection'ından AYRI (o, host'suz consumer;
/// bu, WebApplicationFactory tam host).
/// </summary>
[CollectionDefinition(Name)]
public sealed class NotificationAppCollection : ICollectionFixture<NotificationAppFixture>
{
    public const string Name = "NotificationApp";
}
