using System.Globalization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderProcessingService.Infrastructure;
using OrderHub.OrderProcessingService.Infrastructure.Saga;
using OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

/// <summary>
/// Test B iskeleti: <b>iki ayrı bus, tek gerçek broker</b>.
/// <list type="bullet">
///   <item><b>Saga bus'ı = production DI</b> (<c>AddInfrastructure</c>):
///     EF saga repo (<c>ConcurrencyMode.Optimistic</c> + RowVersion) + <c>UseInMemoryOutbox</c> + retry —
///     hiçbiri test için yeniden tanımlanmaz; yalnızca RabbitMq host/port container'a yöneltilir (config seam).</item>
///   <item><b>Dış dünya bus'ı</b>: saga'nın publish ettiği command'leri yakalayan tek test-double consumer.
///     Diğer servis host'ları dâhil DEĞİL (bu consumer onların yerine geçer).</item>
/// </list>
/// İkisi de aynı container broker'ına bağlanır → publish tip-bazlı exchange routing ile bus'lar arası akar.
/// Saga state <b>gerçek SQL'den</b> (<see cref="SagasSqlServerContainerFixture.CreateContext"/>) okunur.
/// </summary>
internal sealed class SagaBusHarness : IAsyncDisposable
{
    private readonly SagasSqlServerContainerFixture _sql;
    private readonly ServiceProvider _sagaProvider;
    private readonly ServiceProvider _doubleProvider;
    private readonly IBusControl _sagaBus;
    private readonly IBusControl _doubleBus;

    public SagaMessageRecorder Recorder { get; }

    public SagaBusHarness(SagasSqlServerContainerFixture sql, RabbitMqContainerFixture rabbit)
    {
        _sql = sql;
        _sagaProvider = BuildSagaProvider(sql, rabbit);
        _doubleProvider = BuildDoubleProvider(rabbit);
        _sagaBus = _sagaProvider.GetRequiredService<IBusControl>();
        _doubleBus = _doubleProvider.GetRequiredService<IBusControl>();
        Recorder = _doubleProvider.GetRequiredService<SagaMessageRecorder>();
    }

    /// <summary>Her iki bus'ı başlatır (topoloji/binding kurulur) → ilk publish'ten ÖNCE çağrılmalı.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        await _sagaBus.StartAsync(ct);
        await _doubleBus.StartAsync(ct);
    }

    /// <summary>Test mesajını (OrderPlaced/StockReserved/…) dış dünya bus'ından yayımlar → saga tüketir.</summary>
    public Task PublishAsync(object message, CancellationToken ct) => _doubleBus.Publish(message, ct);

    /// <summary>
    /// Saga state <paramref name="predicate"/>'i sağlayana kadar gerçek SQL'i yoklar (bounded — dış CancellationToken
    /// timeout'u sınırlar). Pozitif sinyal → asenkron akışı deterministik kılar (flaky değil).
    /// </summary>
    public async Task<OrderProcessingSagaState> WaitForSagaAsync(
        Guid orderId, Func<OrderProcessingSagaState, bool> predicate, CancellationToken ct)
    {
        while (true)
        {
            await using (var context = _sql.CreateContext())
            {
                var state = await context.SagaStates.AsNoTracking()
                    .FirstOrDefaultAsync(saga => saga.CorrelationId == orderId, ct);
                if (state is not null && predicate(state))
                {
                    return state;
                }
            }

            ct.ThrowIfCancellationRequested();
            await Task.Delay(150, ct);
        }
    }

    private static ServiceProvider BuildSagaProvider(
        SagasSqlServerContainerFixture sql, RabbitMqContainerFixture rabbit)
    {
        // amqp://user:pass@host:port → AddInfrastructure'ın beklediği ayrık config anahtarlarına çevrilir.
        var uri = new Uri(rabbit.ConnectionString);
        var credentials = uri.UserInfo.Split(':', 2);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sql.ConnectionString,
                ["RabbitMq:Host"] = uri.Host,
                ["RabbitMq:Port"] = uri.Port.ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Username"] = Uri.UnescapeDataString(credentials[0]),
                ["RabbitMq:Password"] = Uri.UnescapeDataString(credentials.Length > 1 ? credentials[1] : string.Empty),
                // Test'te kısa interval: incidental retry (ör. redelivery contention) hızlı serileşsin (prod default 1s+).
                ["RabbitMq:Retry:RetryLimit"] = "3",
                ["RabbitMq:Retry:MinInterval"] = "00:00:00.200",
                ["RabbitMq:Retry:MaxInterval"] = "00:00:01",
                ["RabbitMq:Retry:IntervalDelta"] = "00:00:00.100",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration); // prod saga + EF Optimistic repo + InMemoryOutbox + retry.
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildDoubleProvider(RabbitMqContainerFixture rabbit)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SagaMessageRecorder>();
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<SagaCommandRecordingConsumer>();
            bus.UsingRabbitMq((context, rabbitMq) =>
            {
                rabbitMq.Host(new Uri(rabbit.ConnectionString));
                rabbitMq.ConfigureEndpoints(context);
            });
        });
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _sagaBus.StopAsync();
        await _doubleBus.StopAsync();
        await _sagaProvider.DisposeAsync();
        await _doubleProvider.DisposeAsync();
    }
}
