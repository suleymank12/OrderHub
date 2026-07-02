using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.EventBus.RabbitMq;
using OrderHub.OrderProcessingService.Infrastructure.Persistence;
using OrderHub.OrderProcessingService.Infrastructure.Saga;

namespace OrderHub.OrderProcessingService.Infrastructure;

/// <summary>
/// Infrastructure DI kayıtları: <see cref="SagasDbContext"/> (OrderHub_Sagas), MassTransit + RabbitMQ transport
/// (mevcut <c>AddRabbitMqEventBus</c> helper reuse) ve EF saga repository. Connection string yalnızca burada
/// configuration'dan okunur, hard-code edilmez (K3).
/// <para>
/// <b>Saga repository:</b> <see cref="ConcurrencyMode.Optimistic"/> + <c>ExistingDbContext</c> (DI-scoped
/// <see cref="SagasDbContext"/>) → state RowVersion ile korunur. <b>UseInMemoryOutbox</b>: saga consume içinde
/// üretilen mesajlar (ReserveStock/ConfirmOrder/... — 5d-4b) state commit'ine kadar tamponlanır → state-save +
/// send dual-write'ı önlenir. <b>UseMessageRetry</b> (helper'da, en dışta) optimistic contention'ı retry eder
/// → outbox retry'ın içinde kalır (her denemede temiz tampon). State machine transition'ları 5d-4b.
/// </para>
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string RabbitMqSectionName = "RabbitMq";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        // Fail-fast: eksik connection string → net hata (cryptic EF runtime hatası yerine).
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        services.AddDbContext<SagasDbContext>(options => options.UseSqlServer(connectionString));

        var rabbitMqOptions = configuration.GetSection(RabbitMqSectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        services.AddRabbitMqEventBus(
            rabbitMqOptions,
            busConfigurator =>
            {
                busConfigurator.AddSagaStateMachine<OrderProcessingSaga, OrderProcessingSagaState>()
                    .EntityFrameworkRepository(repository =>
                    {
                        repository.ConcurrencyMode = ConcurrencyMode.Optimistic; // RowVersion (ADR-0007 Karar 3).
                        repository.ExistingDbContext<SagasDbContext>();           // DI-scoped DbContext paylaşımı.
                    });
            },
            // In-memory outbox: saga state commit'ine kadar üretilen mesajları tampona alır (dual-write koruması).
            // Helper'da retry'dan SONRA, ConfigureEndpoints'ten ÖNCE uygulanır → retry bunu sarar (her denemede temiz).
            (rabbit, context) => rabbit.UseInMemoryOutbox(context));

        return services;
    }
}
