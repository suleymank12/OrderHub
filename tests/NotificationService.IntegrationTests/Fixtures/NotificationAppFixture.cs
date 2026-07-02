using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.NotificationService.Infrastructure.Notifications;
using OrderHub.NotificationService.Infrastructure.Persistence;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

namespace OrderHub.NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Cart-abandonment e2e fixture (5f-3): <b>tam Program'ı</b> (Kafka consumer + Hangfire server + cold-start
/// migration) gerçek SQL + Kafka container'larına karşı in-process host'lar. Config <b>env var ile</b> verilir:
/// minimal hosting <c>AddInfrastructure</c>/<c>AddHangfireServices</c>'i eager çağırır → <c>ConfigureAppConfiguration</c>
/// (in-memory) DAHA GEÇ çalışır (ApiTestFactory dersi). Kısa <see cref="ReminderDelay"/> → reminder job hızlı fire
/// eder; guard senaryosunda OrderPaid'in projeksiyona uygulanmasına bol margin (consume &lt; 1s ≪ 4s).
/// </summary>
public sealed class NotificationAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // ★ Honest margin (5d-7/5e-3 flaky dersi): 4s delay, OrderPaid consume'u (&lt;1s) fire'dan çok önce biter →
    // guard testinde Paid deterministik olarak fire'dan önce uygulanır; happy testinde job 4s'de fire eder.
    internal static readonly TimeSpan ReminderDelay = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    internal MockEmailSender EmailRecorder => Services.GetRequiredService<MockEmailSender>();

    internal JobStorage JobStorage => Services.GetRequiredService<JobStorage>();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await _kafka.StartAsync();

        // ★ Env var (ApiTestFactory dersi): Program CreateBuilder'da bunları en başta okur; in-memory override geç kalır.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _sql.GetConnectionString());
        Environment.SetEnvironmentVariable("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());
        Environment.SetEnvironmentVariable("Kafka__GroupId", $"notif-e2e-{Guid.NewGuid()}");
        Environment.SetEnvironmentVariable("CartAbandonment__ReminderDelay", ReminderDelay.ToString());

        // Host'u başlat → Program: Development migration + Hangfire schema-prep + consumer + Hangfire server hosted service'leri.
        _ = Services;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(Environments.Development); // startup migration + Hangfire PrepareSchemaIfNecessary.

    internal NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options);

    /// <summary>Order-stream event'ini gerçek Kafka'ya produce eder (4b producer formatı: type-header + JSON value, key = OrderId).</summary>
    internal async Task ProduceAsync(OrderStreamEvent integrationEvent)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();

        var message = new Message<string, string>
        {
            Key = integrationEvent.PartitionKey,
            Value = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), PayloadOptions),
            Headers = new Headers
            {
                { KafkaMessageHeaders.MessageType, Encoding.UTF8.GetBytes(integrationEvent.GetType().FullName!) },
            },
        };

        await producer.ProduceAsync(integrationEvent.Topic, message);
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    public new async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("Kafka__BootstrapServers", null);
        Environment.SetEnvironmentVariable("Kafka__GroupId", null);
        Environment.SetEnvironmentVariable("CartAbandonment__ReminderDelay", null);
        await _sql.DisposeAsync();
        await _kafka.DisposeAsync();
        await base.DisposeAsync();
    }
}
