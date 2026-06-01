using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence.Interceptors;
using OrderHub.OrderService.Infrastructure.Persistence.Repositories;

namespace OrderHub.OrderService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="OrderDbContext"/>, repository ve UnitOfWork.
/// Connection string yalnızca burada (Application'da değil) configuration'dan okunur; hard-code edilmez (K3).
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

        // Stateless → singleton interceptor.
        services.AddSingleton<ClearDomainEventsInterceptor>();

        services.AddDbContext<OrderDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<ClearDomainEventsInterceptor>()));

        services.AddScoped<IOrderRepository, OrderRepository>();

        // IUnitOfWork ve repository AYNI OrderDbContext instance'ını paylaşır (aynı scope) →
        // repo'nun eklediğini UoW'nun save etmesi buna bağlı.
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());

        return services;
    }
}
