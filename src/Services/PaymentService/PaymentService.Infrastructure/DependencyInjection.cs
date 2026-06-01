using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Payments;
using OrderHub.EventBus.RabbitMq;
using OrderHub.Outbox;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Domain.Payments.Events;
using OrderHub.PaymentService.Infrastructure.Messaging;
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
    private const string RabbitMqSectionName = "RabbitMq";

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

        // Outbox WRITE yolu: domain olay → integration event çevirileri. Id = EventId (uçtan uca dedup,
        // ADR-0002 Karar 4 — registry invariant'ı bunu runtime'da doğrular). Money/domain tipi sızdırılmaz.
        services.AddOutboxWriter(registry =>
        {
            registry.Map<PaymentSucceeded>(domainEvent => new PaymentSucceededIntegrationEvent
            {
                Id = domainEvent.EventId,
                OccurredOnUtc = domainEvent.OccurredOnUtc,
                OrderId = domainEvent.OrderId,
                ExternalTransactionId = domainEvent.ExternalTransactionId,
            });

            registry.Map<PaymentFailed>(domainEvent => new PaymentFailedIntegrationEvent
            {
                Id = domainEvent.EventId,
                OccurredOnUtc = domainEvent.OccurredOnUtc,
                OrderId = domainEvent.OrderId,
                Reason = domainEvent.Reason,
            });
        });

        services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                // Pre-commit outbox interceptor (SavingChanges) — domain olayını yalnız OKUR, clear etmez.
                .AddOutboxInterceptor(serviceProvider));

        // Outbox processor port'u → PaymentDbContext (internal olduğundan bu kayıt Infrastructure'da olmalı).
        services.AddScoped<IOutboxDbContext>(serviceProvider => serviceProvider.GetRequiredService<PaymentDbContext>());

        // Transport (RabbitMQ, ADR-0004) + ProcessPayment consumer + outbox processor. RabbitMQ ayarları
        // config'ten (secret env'den, K3). Consumer ProcessPaymentIntegrationEvent'i tüketir; testte
        // ApiTestFactory yok → adanmış in-memory harness consumer'ı kendi kaydeder.
        var rabbitMqOptions = configuration.GetSection(RabbitMqSectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        services.AddRabbitMqEventBus(
            rabbitMqOptions,
            busConfigurator => busConfigurator.AddConsumer<ProcessPaymentIntegrationEventConsumer>());
        services.AddOutboxProcessor();

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // IUnitOfWork ve repository AYNI PaymentDbContext instance'ını paylaşır (aynı scope).
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<PaymentDbContext>());

        return services;
    }
}
