using Testcontainers.Kafka;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek Kafka container'ı (Confluent cp-kafka, KRaft) — Faz 4 producer round-trip + 1:N canlı testi.
/// MsSql/RabbitMq fixture'larıyla aynı Testcontainers pattern'i; image pinli (reproducibility).
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
