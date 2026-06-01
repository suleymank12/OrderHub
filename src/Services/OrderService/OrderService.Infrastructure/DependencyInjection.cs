using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Abstractions.Scheduling;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Infrastructure.BackgroundJobs;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence.Interceptors;
using OrderHub.OrderService.Infrastructure.Persistence.Repositories;
using OrderHub.OrderService.Infrastructure.Scheduling;

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

        // Domain event dispatcher: scoped IPublisher (MediatR) bağımlılığı → scoped interceptor (ADR-0002).
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<OrderDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOrderRepository, OrderRepository>();

        // IUnitOfWork ve repository AYNI OrderDbContext instance'ını paylaşır (aynı scope) →
        // repo'nun eklediğini UoW'nun save etmesi buna bağlı.
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());

        // Faz 2.2 — ödenmeyen sipariş zaman aşımı (ADR-0003 hibrit). OrderTimeoutOptions binding'i
        // composition root'ta (Api) yapılır (JWT pattern'i); burada yalnızca runtime servisleri kayıtlanır.
        // Scheduling port'unun Hangfire adapter'ı (Application Hangfire'ı bilmez, DIP).
        services.AddScoped<IOrderTimeoutScheduler, HangfireOrderTimeoutScheduler>();

        // Read-query port (CQRS read-side projection) — repository'den ayrı (aggregate vs projection).
        services.AddScoped<IOrderSalesQuery, OrderSalesQuery>();

        // Hangfire job'ları DI'dan resolve edilir → scoped (her execute/retry taze DbContext, repo, UoW).
        services.AddScoped<CancelUnpaidOrderJob>();
        services.AddScoped<SweepUnpaidOrdersJob>();
        services.AddScoped<DailySalesReportJob>();
        services.AddScoped<LowStockAlertJob>();

        // Recurring sweep'i startup'ta idempotent kaydeder (tüm ortamlar).
        services.AddHostedService<RecurringJobRegistrar>();

        return services;
    }
}
