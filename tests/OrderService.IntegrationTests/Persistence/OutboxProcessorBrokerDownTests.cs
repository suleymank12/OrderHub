using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Payments;
using OrderHub.EventBus;
using OrderHub.Outbox;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// Faz 3 Adım 3d-4a — OutboxProcessor broker-down düzeltmesinin <b>davranış</b> testi (gerçek SQL container,
/// gerçek processor hosted service; broker YOK — <see cref="TogglableIntegrationEventPublisher"/> stub). Bu,
/// processor'ın <b>ilk kez gerçekten start edildiği</b> testtir (bugüne dek yalnız outbox YAZMA tarafı test edildi).
/// Kanıtlar: (★) broker-down'da <c>RetryCount</c> ARTMAZ (eski bug'da 5'e ulaşıp kalıcı düşerdi = veri kaybı) +
/// broker dönünce publish olur; publish-timeout fail-fast (bloke broker) da transient; <b>poison</b> (deserialize
/// hatası) ise terminal kalır → <c>MaxRetryCount</c>'ta DLQ. İki yol bir arada = ayrımın doğru çalıştığı kanıtı.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxProcessorBrokerDownTests(SqlServerContainerFixture fixture)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BrokerDownThrows_DefersPublish_RetryCountStaysZero_ThenPublishesWhenHealthy()
    {
        var setup = BuildProvider();
        await using var provider = setup.Provider;
        var publisher = setup.Publisher;
        publisher.Mode = OutboxPublisherMode.BrokerDownThrows;

        var ids = await SeedValidOutboxRowsAsync(3);
        try
        {
            await RunProcessorAsync(provider, async () =>
            {
                // Down: birçok poll geçsin (150ms poll → ~1.5sn ≈ 10 poll). Eski bug'da RetryCount 5'e ulaşıp düşerdi.
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                foreach (var id in ids)
                {
                    var message = await LoadAsync(id);
                    message.ProcessedOnUtc.Should().BeNull("broker down → publish edilemez");
                    message.RetryCount.Should().Be(0, "★ publish transient → RetryCount ARTMAMALI (3d-4 bug düzeldi)");
                }

                // Recover: broker up → bekleyen mesajlar publish olmalı (sayaç hiç şişmeden).
                publisher.Mode = OutboxPublisherMode.Healthy;
                await WaitUntilAllProcessedAsync(ids, TimeSpan.FromSeconds(15));
                foreach (var id in ids)
                {
                    var message = await LoadAsync(id);
                    message.ProcessedOnUtc.Should().NotBeNull("broker dönünce publish olmalı");
                    message.RetryCount.Should().Be(0, "transient dönem RetryCount'u şişirmemeli");
                }
            });
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task BrokerDownBlocks_PublishTimesOut_RetryCountStaysZero_ThenPublishesWhenHealthy()
    {
        var setup = BuildProvider();
        await using var provider = setup.Provider;
        var publisher = setup.Publisher;
        publisher.Mode = OutboxPublisherMode.BrokerDownBlocks; // publish bloke → PublishTimeout fail-fast etmeli.

        var ids = await SeedValidOutboxRowsAsync(2);
        try
        {
            await RunProcessorAsync(provider, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                foreach (var id in ids)
                {
                    var message = await LoadAsync(id);
                    message.ProcessedOnUtc.Should().BeNull("bloke publish → timeout → publish edilemez");
                    message.RetryCount.Should().Be(0, "★ timeout transient → RetryCount ARTMAMALI (poll döngüsü asılmadı)");
                }

                publisher.Mode = OutboxPublisherMode.Healthy;
                await WaitUntilAllProcessedAsync(ids, TimeSpan.FromSeconds(15));
                (await LoadAsync(ids[0])).RetryCount.Should().Be(0);
            });
        }
        finally
        {
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task PoisonMessage_FailsDeserialize_IncrementsRetryCount_UntilDeadLettered_WhileValidIsProcessed()
    {
        const int maxRetryCount = 3;
        var setup = BuildProvider(maxRetryCount);
        await using var provider = setup.Provider;
        setup.Publisher.Mode = OutboxPublisherMode.Healthy; // broker up: geçerli satır publish olur, poison deserialize'da düşer.

        var poisonId = await SeedPoisonOutboxRowAsync();
        var validIds = await SeedValidOutboxRowsAsync(2);
        var allIds = validIds.Append(poisonId).ToList();
        try
        {
            await RunProcessorAsync(provider, async () =>
            {
                // Poison: deserialize hep fail → RetryCount Max'a kadar artar, sonra sorgudan düşer (terminal/DLQ).
                var poison = await WaitUntilRetryCountAsync(poisonId, maxRetryCount, TimeSpan.FromSeconds(15));
                poison.ProcessedOnUtc.Should().BeNull("poison publish edilemez");
                poison.RetryCount.Should().Be(maxRetryCount, "deserialize terminal → MaxRetryCount'ta DLQ (sorgudan düşer)");

                // Max'a ulaştıktan sonra sorgu artık çekmez → RetryCount sabit kalmalı (sonsuz artış yok).
                await Task.Delay(TimeSpan.FromSeconds(1));
                (await LoadAsync(poisonId)).RetryCount.Should().Be(maxRetryCount, "DLQ'lanan mesaj artık denenmez");

                // Aynı batch'te geçerli satırlar publish olmalı (ayrım çalışıyor: poison terminal, geçerli akıyor).
                await WaitUntilAllProcessedAsync(validIds, TimeSpan.FromSeconds(15));
                foreach (var id in validIds)
                {
                    (await LoadAsync(id)).ProcessedOnUtc.Should().NotBeNull("geçerli mesaj broker up'ta publish olmalı");
                }
            });
        }
        finally
        {
            await CleanupAsync(allIds);
        }
    }

    // Processor'ı GERÇEKTEN start eder (hosted service manuel StartAsync → ExecuteAsync). Full Host gerekmez.
    private static async Task RunProcessorAsync(ServiceProvider provider, Func<Task> body)
    {
        var processor = provider.GetServices<IHostedService>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            await body();
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    private (ServiceProvider Provider, TogglableIntegrationEventPublisher Publisher) BuildProvider(int maxRetryCount = 5)
    {
        var publisher = new TogglableIntegrationEventPublisher();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OrderDbContext>(options => options.UseSqlServer(fixture.ConnectionString));
        services.AddScoped<IOutboxDbContext>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());
        services.AddSingleton<IIntegrationEventPublisher>(publisher);
        services.AddOutboxProcessor(processorOptions =>
        {
            processorOptions.PollingInterval = TimeSpan.FromMilliseconds(150);
            processorOptions.PublishTimeout = TimeSpan.FromMilliseconds(300); // bloke publish'i hızlı kessin (test).
            processorOptions.MaxRetryCount = maxRetryCount;
        });

        return (services.BuildServiceProvider(), publisher);
    }

    private async Task<List<Guid>> SeedValidOutboxRowsAsync(int count)
    {
        var ids = new List<Guid>();
        await using var context = fixture.CreateContext();
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

    private async Task<Guid> SeedPoisonOutboxRowAsync()
    {
        var id = Guid.NewGuid();
        await using var context = fixture.CreateContext();
        // Çözülemez CLR tipi → OutboxMessageSerializer.Deserialize fail-fast (gerçek poison).
        context.OutboxMessages.Add(OutboxMessage.Create(id, "OrderHub.Bogus.NotARealType, OrderHub.Bogus", "{}", DateTime.UtcNow));
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using var context = fixture.CreateContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id);
    }

    private async Task WaitUntilAllProcessedAsync(IReadOnlyCollection<Guid> ids, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            await using var context = fixture.CreateContext();
            var processed = await context.OutboxMessages.AsNoTracking()
                .CountAsync(message => ids.Contains(message.Id) && message.ProcessedOnUtc != null, cts.Token);
            if (processed == ids.Count)
            {
                return;
            }

            await Task.Delay(150, cts.Token);
        }
    }

    private async Task<OutboxMessage> WaitUntilRetryCountAsync(Guid id, int target, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            var message = await LoadAsync(id);
            if (message.RetryCount >= target)
            {
                return message;
            }

            await Task.Delay(150, cts.Token);
        }
    }

    private async Task CleanupAsync(IEnumerable<Guid> ids)
    {
        await using var context = fixture.CreateContext();
        foreach (var id in ids)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OutboxMessages WHERE Id = {id}");
        }
    }
}
