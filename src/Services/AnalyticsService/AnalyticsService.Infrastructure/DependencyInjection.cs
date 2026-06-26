using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Infrastructure.Messaging;
using OrderHub.AnalyticsService.Infrastructure.Persistence;

namespace OrderHub.AnalyticsService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="AnalyticsDbContext"/> (read-model) + Kafka order-stream
/// consumer (<see cref="OrderEventsConsumer"/>). Connection string + Kafka ayarları yalnız burada config'ten
/// okunur (K3). Outbox/Inbox/MassTransit YOK (terminal consumer, event üretmez). İdempotency/revenue 4c-3'te.
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string KafkaBootstrapServersKey = "Kafka:BootstrapServers";
    private const string KafkaGroupIdKey = "Kafka:GroupId";
    private const string DefaultKafkaBootstrapServers = "localhost:9092";
    private const string DefaultKafkaGroupId = "analytics-service.order-events";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        // Fail-fast: eksik connection string → net hata (cryptic EF runtime hatası yerine).
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        services.AddDbContext<AnalyticsDbContext>(options => options.UseSqlServer(connectionString));

        // Read-side port (4d): query handler'lar bu abstraction üzerinden projection okur (Clean Arch — Application
        // DbContext'i bilmez). Scoped: DbContext ile aynı yaşam döngüsü.
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();

        // Kafka consumer (ROADMAP §4.4): sabit group + earliest (birikmiş event'leri işle) + manual commit (commit-after).
        var bootstrapServers = configuration[KafkaBootstrapServersKey] ?? DefaultKafkaBootstrapServers;
        var groupId = configuration[KafkaGroupIdKey] ?? DefaultKafkaGroupId;
        services.AddSingleton<IConsumer<string, string>>(_ => new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // manual Commit, DB commit'ten SONRA (at-least-once).
        }).Build());
        services.AddHostedService<OrderEventsConsumer>();

        return services;
    }
}
