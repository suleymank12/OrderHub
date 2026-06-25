using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.AnalyticsService.Infrastructure.Persistence;

namespace OrderHub.AnalyticsService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="AnalyticsDbContext"/> (read-model persistence).
/// Connection string yalnız burada configuration'dan okunur; hard-code edilmez (K3). 4c-1 iskelet: yalnız
/// DbContext — Kafka consumer (HostedService) 4c-2'de, Outbox/Inbox/MassTransit YOK (terminal consumer).
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";

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

        return services;
    }
}
