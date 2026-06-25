using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderHub.EventBus.RabbitMq;
using OrderHub.Inbox.Consuming;
using OrderHub.PaymentService.Application;
using OrderHub.PaymentService.Application.Payments.Configuration;
using OrderHub.PaymentService.Infrastructure;
using OrderHub.PaymentService.Infrastructure.Persistence;
using OrderHub.PaymentService.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OrderHub.PaymentService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3d-3 — <b>gerçek RabbitMQ</b> üzerinde consumer-side retry + <c>_error</c> DLQ + inbox etkileşimi.
/// Test, production wiring'i (<see cref="EventBusServiceCollectionExtensions.AddRabbitMqEventBus"/>: retry EN DIŞTA,
/// sonra inbox filter) <b>aynen</b> çalıştırır; yalnız retry interval'ini ms'ye düşürür (exponential 1s/30s testi
/// dakikalarca beklerdi). Kanıtlar: (a) consumer 6 kez çağrıldı (1 + 5 retry), (b) retry tükenince mesaj gerçek
/// <c>&lt;queue&gt;_error</c> queue'ya düştü (doğrudan okunur), (c) hiçbir denemede inbox satırı commit edilmedi
/// (her deneme rollback → temiz tekrar). Tek test (yavaş): gerçek broker + retry zinciri. 3c-4 fixture pattern'i.
/// </summary>
[Collection(PaymentEndToEndCollection.Name)]
public sealed class RetryDeadLetterEndToEndTests(
    PaymentSqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    private const string FailingEndpointName = "retry-dlq-test";
    private const string FailingErrorQueue = FailingEndpointName + "_error"; // MassTransit DLQ convention.
    private static readonly string FailingMessageType = typeof(FailingTestIntegrationEvent).FullName!;

    [Fact]
    public async Task FailingConsumer_ExhaustsRetries_LandsInErrorQueue_AndCommitsNoInboxRow()
    {
        var probe = new RetryProbe();
        var eventId = Guid.NewGuid();
        await using var provider = BuildProvider(probe);
        var busControl = provider.GetRequiredService<IBusControl>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await busControl.StartAsync(timeout.Token);

        try
        {
            // Gerçek broker kanıtı: in-memory loopback DEĞİL → MassTransit RabbitMQ transport.
            busControl.Address.Scheme.Should().Be("rabbitmq");

            await busControl.Publish(
                new FailingTestIntegrationEvent { Id = eventId, OccurredOnUtc = DateTime.UtcNow },
                timeout.Token);

            // (b) Retry tükenince MassTransit mesajı <queue>_error'a (DLQ) taşır → gerçek queue'dan oku (literal kanıt).
            var landedInDlq = await WaitForErrorQueueMessageAsync(rabbit.ConnectionString, FailingErrorQueue, timeout.Token);
            landedInDlq.Should().BeTrue("retry tükenince mesaj _error DLQ'ya düşmeli");

            // (a) Retry gerçekten N kez denendi: 1 ilk + RetryLimit(5) = 6 consume. DLQ'ya düştüyse zincir bitmiştir.
            probe.Attempts.Should().Be(6, "1 ilk deneme + 5 exponential retry = 6 consume; sonra _error DLQ");

            // (c) Inbox etkileşimi: her deneme SaveChanges'siz throw → tracked inbox Add rollback → commit edilmiş
            // satır YOK. 6 consume'un hepsinin çalışması (sayaç=6) inbox filter'ın hiç skip etmediğini, yani her
            // retry'ın taze consume-scope'ta temiz tekrarlandığını kanıtlar (retry EN DIŞTA, ADR-0005 Karar 4).
            (await InboxRowExistsAsync(provider, eventId)).Should()
                .BeFalse("başarısız denemeler rollback olur → inbox yalnız COMMIT edilmiş consume'ları kaydeder");
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }

    // Gerçek _error queue'dan oku (RabbitMQ.Client, transitive). MassTransit error queue'yu endpoint başlangıcında
    // declare eder; yine de NOT_FOUND yarışına karşı kanalı her denemede tazeleyip OperationInterrupted'ı yutar.
    private static async Task<bool> WaitForErrorQueueMessageAsync(
        string connectionString, string errorQueue, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                var result = await channel.BasicGetAsync(errorQueue, autoAck: true, cancellationToken);
                if (result is not null)
                {
                    return true;
                }
            }
            catch (OperationInterruptedException)
            {
                // _error queue henüz declare edilmedi (ilk fault'tan önce) → tekrar dene.
            }

            await Task.Delay(200, cancellationToken);
        }
    }

    private static async Task<bool> InboxRowExistsAsync(IServiceProvider provider, Guid eventId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .InboxMessages.AsNoTracking()
            .AnyAsync(m => m.MessageId == eventId && m.MessageType == FailingMessageType);
    }

    // Gerçek production DI (PaymentDbContext + AddInbox + IInboxDbContext). Prod (localhost) MassTransit kaydını
    // çıkarıp test container'ına + KISA retry'a yeniden kurar. AddRabbitMqEventBus kullanılır → retry-EN-DIŞTA
    // sırası building block'tan gelir (reimplementation değil — gerçek prod kod yolu doğrulanır).
    private ServiceProvider BuildProvider(RetryProbe probe)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton(probe);
        services.AddSingleton<IOptions<MockPaymentOptions>>(
            Options.Create(new MockPaymentOptions { SuccessRatePercent = 100 }));

        RemoveMassTransitRegistrations(services); // prod (localhost) MassTransit → çıkar, gerçek container ile yeniden kur.

        var uri = new Uri(rabbit.ConnectionString); // amqp://user:pass@host:port
        var userInfo = uri.UserInfo.Split(':', 2);
        var options = new RabbitMqOptions
        {
            Host = uri.Host,
            Port = (ushort)uri.Port,
            Username = userInfo[0],
            Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
            // Test-özel KISA retry: production 1s/30s exponential testi ~dakikalarca bekletirdi. Policy ŞEKLİ
            // (exponential, limit 5, retry EN DIŞTA) production ile AYNI; yalnız interval'ler ms (hızlı bitsin).
            Retry = new RabbitMqRetryOptions
            {
                RetryLimit = 5,
                MinInterval = TimeSpan.FromMilliseconds(5),
                MaxInterval = TimeSpan.FromMilliseconds(50),
                IntervalDelta = TimeSpan.FromMilliseconds(5),
            },
        };

        // Production parite: inbox filter consume-pipe'a (retry'dan SONRA — building block içinde retry önce eklenir).
        // Endpoint adı sabit → _error queue adı deterministik (retry-dlq-test_error).
        services.AddRabbitMqEventBus(
            options,
            busConfigurator => busConfigurator
                .AddConsumer<FailingTestConsumer>()
                .Endpoint(endpoint => endpoint.Name = FailingEndpointName),
            (rabbitMq, context) => rabbitMq.UseConsumeFilter(typeof(InboxConsumeFilter<>), context));

        return services.BuildServiceProvider();
    }

    private static void RemoveMassTransitRegistrations(ServiceCollection services)
    {
        var descriptors = services
            .Where(d =>
                IsMassTransitType(d.ServiceType) ||
                IsMassTransitType(d.ImplementationType) ||
                IsMassTransitType(d.ImplementationInstance?.GetType()))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsMassTransitType(Type? type) =>
        type?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;
}
