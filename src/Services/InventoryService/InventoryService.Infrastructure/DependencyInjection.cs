using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.InventoryService.Infrastructure.Persistence;

namespace OrderHub.InventoryService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları. 5b iskeleti: yalnız <see cref="InventoryDbContext"/> (OrderHub_Inventory
/// şeması). Connection string yalnızca burada (Application'da değil) configuration'dan okunur; hard-code edilmez (K3).
/// <b>Repository + outbox/inbox + MassTransit consumer'lar 5c'de</b> (komut handler'larıyla birlikte) eklenir —
/// kullanılmayan port/altyapı şimdi taşınmaz (dead-code kaçınma, AnalyticsService 4c-1 dersi).
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

        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }
}
