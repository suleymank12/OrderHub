using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3b-2 — gerçek RabbitMQ transport smoke testi. ApiTestFactory'nin in-memory harness'ini
/// KULLANMAZ; kendi MassTransit bus'ını fixture'ın gerçek broker container'ına (<c>UsingRabbitMq</c>) bağlar.
/// Amaç dar: bir mesaj broker üzerinden publish→consume oluyor mu + transport gerçekten <c>rabbitmq</c> mı
/// (loopback değil). Consumer/inbox/DLQ logic'i burada YOK (3c/3d).
/// </summary>
[Collection(MessagingCollection.Name)]
public sealed class RabbitMqSmokeTests(RabbitMqContainerFixture fixture)
{
    [Fact]
    public async Task PublishedMessage_RoundTripsThroughRealRabbitMqBroker()
    {
        var signal = new SmokeSignal();

        await using var provider = new ServiceCollection()
            .AddSingleton(signal) // consumer ile test aynı instance'ı paylaşsın.
            .AddMassTransit(configurator =>
            {
                configurator.AddConsumer<SmokeConsumer>();
                configurator.UsingRabbitMq((context, rabbit) =>
                {
                    rabbit.Host(new Uri(fixture.ConnectionString)); // gerçek broker (amqp://...).
                    rabbit.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider();

        var busControl = provider.GetRequiredService<IBusControl>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await busControl.StartAsync(timeout.Token);

        try
        {
            // Gerçek broker kanıtı: in-memory loopback DEĞİL → MassTransit RabbitMQ transport.
            busControl.Address.Scheme.Should().Be("rabbitmq");

            var messageId = Guid.NewGuid();
            await busControl.Publish(new SmokeMessage(messageId), timeout.Token);

            // Consumer GERÇEKTEN broker üzerinden tüketmeli (timeout'lu bekleme → asılı kalmaz).
            var consumed = await signal.Completion.WaitAsync(TimeSpan.FromSeconds(20), timeout.Token);
            consumed.Id.Should().Be(messageId);
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }
}
