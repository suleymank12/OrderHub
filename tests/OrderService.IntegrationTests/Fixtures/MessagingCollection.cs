namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// RabbitMQ smoke testleri için xUnit collection tanımı: <see cref="RabbitMqContainerFixture"/> session
/// başına bir kez kurulur. <see cref="DatabaseCollection"/>'dan AYRIDIR (tek sorumluluk: DB testleri broker
/// beklemesin, broker testleri DB beklemesin). Assembly genelinde paralelizasyon kapalı → iki container
/// (SQL + RabbitMQ) çakışmadan, seri çalışır.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "Messaging";
}
