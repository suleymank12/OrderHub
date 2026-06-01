using Testcontainers.RabbitMq;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek RabbitMQ container'ı (Testcontainers) ayağa kaldıran fixture — <see cref="SqlServerContainerFixture"/>
/// ile birebir pattern (IAsyncLifetime + collection fixture). Yalnızca <b>transport smoke testi</b> içindir;
/// mevcut Api/Infra testleri broker beklemesin diye ayrı bir collection'a (<see cref="MessagingCollection"/>)
/// bağlanır. Image pinli (reproducibility). Connection string container'ın kimlik bilgilerini taşır.
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management")
        .Build();

    /// <summary>AMQP connection string (amqp://user:pass@host:port) — kimlik bilgilerini içerir.</summary>
    internal string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
