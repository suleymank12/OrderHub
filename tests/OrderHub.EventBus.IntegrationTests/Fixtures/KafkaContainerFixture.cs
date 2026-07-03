using Testcontainers.Kafka;

namespace OrderHub.EventBus.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek Kafka container'ı (Confluent cp-kafka, KRaft) — 6c-2 trace propagation e2e testi için. Diğer servislerdeki
/// eşiyle aynı pattern; ayrı assembly olduğundan fixture paylaşılamaz. Image pinli (compose ile aynı major.minor).
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
