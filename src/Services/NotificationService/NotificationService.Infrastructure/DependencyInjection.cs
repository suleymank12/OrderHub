using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.NotificationService.Infrastructure.Messaging;
using OrderHub.NotificationService.Infrastructure.Persistence;

namespace OrderHub.NotificationService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="NotificationDbContext"/> (read-model + inbox dedup) +
/// Kafka order-stream consumer (<see cref="OrderEventsConsumer"/>). Connection string + Kafka ayarları yalnız
/// burada config'ten okunur (K3). ★ GroupId <c>notification-service.order-events</c>: AnalyticsService'in
/// <c>analytics-service.order-events</c>'inden FARKLI → iki consumer aynı topic'ten bağımsız okur (fan-out).
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string KafkaBootstrapServersKey = "Kafka:BootstrapServers";
    private const string KafkaGroupIdKey = "Kafka:GroupId";
    private const string DefaultKafkaBootstrapServers = "localhost:9092";
    private const string DefaultKafkaGroupId = "notification-service.order-events";

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

        services.AddDbContext<NotificationDbContext>(options => options.UseSqlServer(connectionString));

        // Kafka consumer: sabit group + earliest (birikmiş event'leri işle) + manual commit (commit-after).
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
