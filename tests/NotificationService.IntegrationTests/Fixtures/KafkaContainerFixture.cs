using Testcontainers.Kafka;

namespace OrderHub.NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek Kafka container'ı (Confluent cp-kafka, KRaft) — 5f-1 consumer round-trip testi. Image pinli,
/// AnalyticsService.IntegrationTests'deki fixture'la birebir aynı pattern (ayrı assembly → yeniden tanımlanır).
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
