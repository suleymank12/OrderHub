using Testcontainers.RabbitMq;

namespace OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek RabbitMQ container'ı (5d-7 saga uçtan uca testi, Test B). Diğer servislerin IntegrationTests
/// fixture'larıyla aynı pattern; ayrı assembly olduğundan instance paylaşılamaz. Image pinli (compose ile aynı).
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management")
        .Build();

    /// <summary>AMQP connection string (amqp://user:pass@host:port).</summary>
    internal string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
