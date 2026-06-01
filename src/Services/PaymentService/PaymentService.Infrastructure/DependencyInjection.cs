using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Outbox;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Infrastructure.Persistence;
using OrderHub.PaymentService.Infrastructure.Persistence.Repositories;

namespace OrderHub.PaymentService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="PaymentDbContext"/>, repository, UnitOfWork ve outbox
/// yazma yolu. Connection string yalnızca burada configuration'dan okunur; hard-code edilmez (K3).
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

        // Outbox WRITE yolu: registry şimdilik BOŞ — PaymentSucceeded/Failed → integration event mapping
        // 3c scope'u (RabbitMQ ile birlikte). BOŞ registry K2 ihlali değil: şema + writer yolu burada hazır
        // edilir (migration OutboxMessages'ı üretir), yalnızca Map çağrıları 3c'ye ertelenir (ROADMAP'te dokümante).
        services.AddOutboxWriter(_ => { });

        services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                // Pre-commit outbox interceptor (SavingChanges) — domain olayını yalnız OKUR, clear etmez.
                .AddOutboxInterceptor(serviceProvider));

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // IUnitOfWork ve repository AYNI PaymentDbContext instance'ını paylaşır (aynı scope).
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<PaymentDbContext>());

        return services;
    }
}
