using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Application.Abstractions.Persistence;
using OrderHub.NotificationService.Application.Notifications;
using OrderHub.NotificationService.Infrastructure.Messaging;
using OrderHub.NotificationService.Infrastructure.Notifications;
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

        // ★ E-posta gönderimi (5f-2): MockEmailSender singleton → IEmailSender alias.
        // Singleton: thread-safe, tüm scope'lardan tek instance → integration testlerde Sent gözlemlenebilir.
        services.AddSingleton<MockEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<MockEmailSender>());

        // Bildirim projection repository: EF Core DbContext ile uyumlu scoped yaşam süresi.
        services.AddScoped<INotificationOrderRepository, NotificationOrderRepository>();

        // CartAbandonmentReminderJob: Hangfire DI-resolved job (scoped; her job çalışmasında yeni örnek).
        // ICartAbandonmentScheduler (ve Hangfire.Core) Api katmanında kayıtlıdır — burada YOK.
        services.AddScoped<CartAbandonmentReminderJob>();

        return services;
    }
}
