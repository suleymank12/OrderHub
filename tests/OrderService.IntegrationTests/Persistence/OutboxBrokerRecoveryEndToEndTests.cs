using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.Contracts.Payments;
using OrderHub.EventBus.RabbitMq;
using OrderHub.Outbox;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using Testcontainers.RabbitMq;
using Xunit.Abstractions;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// Faz 3 Adım 3d-4b — §3.8 kabul testi: <b>gerçek RabbitMQ</b> down → outbox birikir → ayağa kalkınca publish.
/// 3d-4a fix'inin (transient publish → RetryCount artmaz → broker dönünce publish) gerçek broker'daki faithful
/// doğrulaması + durum tespitindeki ampirik sorunun cevabı (MassTransit broker-down'da publish'i fırlatıyor mu,
/// bloke mi). <b>Dedicated</b> RabbitMQ container (test kendi yönetir; paylaşılan fixture'lara DOKUNMAZ) —
/// AYNI-instance Stop/Start ile port korunur → connection string sabit → MassTransit reconnect (flakiness'siz).
/// SQL paylaşılan fixture'dan (durdurulmaz). Bus broker UP iken start edilir (cold-start-against-dead-broker
/// belirsizliğini eler; §3.8 "kesinti sırasında down" modeline de daha sadık). Tek test (yavaş: start+stop+start+reconnect).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxBrokerRecoveryEndToEndTests(SqlServerContainerFixture sql, ITestOutputHelper output)
{
    private const int PublishDeferredEventId = 3004; // OutboxLog.PublishDeferred (transient deferral sinyali).
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BrokerOutage_OutboxAccumulates_ThenPublishesOnRecovery_OverRealBroker()
    {
        // ★ Sabit (pinned) host port: Testcontainers'ın default ephemeral mapping'i stop/start'ta DEĞİŞİYOR
        // (gözlemlendi: 63449→63456) → MassTransit eski porta reconnect dener, asla bağlanamaz. Pinned port,
        // connection string'i restart boyunca SABİT tutar → reconnect deterministik. Serbest port runtime'da seçilir.
        var hostPort = GetFreeTcpPort();
        var rabbit = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management")
            .WithPortBinding(hostPort, 5672)
            .Build();
        await rabbit.StartAsync();
        var connectionString = rabbit.GetConnectionString(); // Pinned port → stop/start boyunca sabit.
        var capture = new CapturingLoggerProvider();
        var ids = new List<Guid>();

        try
        {
            var provider = BuildProvider(connectionString, capture);
            await using (provider)
            {
                var hostedServices = provider.GetServices<IHostedService>().ToList();
                // Bus + processor'ı broker UP iken start et → bus temiz bağlanır (dead-broker cold-start flakiness'i yok).
                using (var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    foreach (var hostedService in hostedServices)
                    {
                        await hostedService.StartAsync(startCts.Token);
                    }
                }

                try
                {
                    // (2) §3.8 kesinti: bus bağlıyken broker'ı durdur (gerçek outage modeli).
                    await rabbit.StopAsync();

                    // (3) Outbox'a yaz (broker gerekmez, sadece DB). Processor bunları publish etmeye çalışacak → fail (transient).
                    ids = await SeedValidOutboxRowsAsync(2);

                    // (5) Down: en az bir publish denemesi DEFERRED olana kadar bekle (deterministik sinyal), sonra state assert.
                    await WaitUntilDeferredAsync(capture, TimeSpan.FromSeconds(45));
                    foreach (var id in ids)
                    {
                        var message = await LoadAsync(id);
                        message.ProcessedOnUtc.Should().BeNull("broker down → publish edilemez (birikir, §3.8)");
                        message.RetryCount.Should().Be(0, "★ transient → RetryCount ARTMAZ (gerçek broker'da da, 3d-4a fix)");
                    }

                    // (6) Broker ayağa kalk (AYNI pinned port → connection string sabit → MassTransit otomatik reconnect).
                    await rabbit.StartAsync();

                    // (7) Publish polling (sabit Task.Delay DEĞİL): broker-ready + reconnect + publish olana dek poll.
                    //     Ayrı port-probe yerine publish başarısına güveniriz: publish OLDUYSA broker zaten hazırdı (asıl ilgilenilen koşul).
                    await WaitUntilAllProcessedAsync(ids, TimeSpan.FromSeconds(75));
                    foreach (var id in ids)
                    {
                        var message = await LoadAsync(id);
                        message.ProcessedOnUtc.Should().NotBeNull("★ ayağa kalkınca otomatik publish (§3.8 ana kanıt)");
                        message.RetryCount.Should().Be(0, "transient dönem RetryCount'u şişirmemeli");
                    }

                    // Ampirik bulgu: ilk deferred'ın exception tipi → fırlattı (connection ex) mı, bloke→timeout (TaskCanceled) mu.
                    var firstDeferred = capture.Logs.FirstOrDefault(log => log.EventId == PublishDeferredEventId);
                    output.WriteLine($"EMPIRICAL: first PublishDeferred exception type = {firstDeferred?.ExceptionType ?? "<none>"}");
                }
                finally
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    for (var i = hostedServices.Count - 1; i >= 0; i--)
                    {
                        await hostedServices[i].StopAsync(stopCts.Token);
                    }
                }
            }
        }
        finally
        {
            await CleanupAsync(ids);
            await rabbit.DisposeAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private ServiceProvider BuildProvider(string connectionString, CapturingLoggerProvider capture)
    {
        var uri = new Uri(connectionString); // amqp://user:pass@host:port
        var userInfo = uri.UserInfo.Split(':', 2);
        var rabbitMqOptions = new RabbitMqOptions
        {
            Host = uri.Host,
            Port = (ushort)uri.Port,
            Username = userInfo[0],
            Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
        };

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(capture));
        services.AddDbContext<OrderDbContext>(options => options.UseSqlServer(sql.ConnectionString));
        services.AddScoped<IOutboxDbContext>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());

        // Gerçek publish yolu: AddRabbitMqEventBus → MassTransitIntegrationEventPublisher + bus (publish-only, consumer yok).
        services.AddRabbitMqEventBus(rabbitMqOptions);
        services.AddOutboxProcessor(processorOptions =>
        {
            processorOptions.PollingInterval = TimeSpan.FromMilliseconds(200);
            processorOptions.PublishTimeout = TimeSpan.FromSeconds(3); // broker bloke ederse fail-fast (test makul kalsın).
        });

        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilDeferredAsync(CapturingLoggerProvider capture, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!capture.Logs.Any(log => log.EventId == PublishDeferredEventId))
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(200, cts.Token);
        }
    }

    private async Task<List<Guid>> SeedValidOutboxRowsAsync(int count)
    {
        var ids = new List<Guid>();
        await using var context = sql.CreateContext();
        for (var i = 0; i < count; i++)
        {
            var integrationEvent = new ProcessPaymentIntegrationEvent
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = 100m,
                Currency = "TRY",
            };
            var payload = JsonSerializer.Serialize(integrationEvent, PayloadOptions);
            context.OutboxMessages.Add(OutboxMessage.Create(
                integrationEvent.Id,
                typeof(ProcessPaymentIntegrationEvent).AssemblyQualifiedName!,
                payload,
                integrationEvent.OccurredOnUtc));
            ids.Add(integrationEvent.Id);
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using var context = sql.CreateContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id);
    }

    private async Task WaitUntilAllProcessedAsync(IReadOnlyCollection<Guid> ids, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using var context = sql.CreateContext();
            var processed = await context.OutboxMessages.AsNoTracking()
                .CountAsync(message => ids.Contains(message.Id) && message.ProcessedOnUtc != null, cts.Token);
            if (processed == ids.Count)
            {
                return;
            }

            await Task.Delay(250, cts.Token);
        }
    }

    private async Task CleanupAsync(IEnumerable<Guid> ids)
    {
        await using var context = sql.CreateContext();
        foreach (var id in ids)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OutboxMessages WHERE Id = {id}");
        }
    }
}
