using Testcontainers.Kafka;

namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek Kafka container'ı (Confluent cp-kafka, KRaft) — Faz 4 consumer round-trip testi. OrderService'teki
/// eşiyle aynı pattern; ayrı assembly olduğundan fixture instance'ı paylaşılamaz → burada da tanımlanır. Image pinli.
/// </summary>
public sealed class KafkaContainerFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    /// <summary>Producer/consumer'ın bağlanacağı bootstrap adres(ler)i (host:port).</summary>
    internal string BootstrapServers => _container.GetBootstrapAddress();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
