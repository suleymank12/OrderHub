using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Inventory;
using OrderHub.EventBus.RabbitMq;
using OrderHub.Inbox;
using OrderHub.Inbox.Consuming;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Stock.BackgroundJobs;
using OrderHub.InventoryService.Domain.Stock.Events;
using OrderHub.InventoryService.Infrastructure.BackgroundJobs;
using OrderHub.InventoryService.Infrastructure.Messaging;
using OrderHub.InventoryService.Infrastructure.Persistence;
using OrderHub.InventoryService.Infrastructure.Persistence.Repositories;
using OrderHub.Outbox;
using OrderHub.Outbox.Translation;

namespace OrderHub.InventoryService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: InventoryDbContext, repository, UnitOfWork, outbox
/// yazma yolu, inbox dedup, RabbitMQ transport ve Hangfire recurring job.
/// Connection string yalnızca burada configuration'dan okunur; hard-code edilmez (K3).
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
        // ADR-0002 Karar 4 — registry invariant'ı bunu runtime'da doğrular).
        // StockReservationConfirmed KASITLI haritaLANMAZ: yalnızca in-process sinyaldir, saga
        // ConfirmStockReservation'dan sonra zaten Completed'a geçer; integration event yoktur.
        services.AddOutboxWriter(ConfigureOutboxMaps);

        services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                // Pre-commit outbox interceptor (SavingChanges) — domain olayını yalnız OKUR, clear etmez.
                .AddOutboxInterceptor(serviceProvider));

        // Outbox processor + inbox dedup port'ları → InventoryDbContext (internal → kayıt Infrastructure'da).
        // İkisi de AYNI scope'ta aynı InventoryDbContext → inbox filter Add + handler SaveChanges atomik (ADR-0005 Karar 3).
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());
        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());
        services.AddInbox();

        // Transport (RabbitMQ, ADR-0004) + 3 consumer + inbox dedup filter + outbox processor.
        // Inbox filter (consume-pipe) consumer'dan önce duplicate'leri keser (ADR-0005). RabbitMQ ayarları config'ten (K3).
        var rabbitMqOptions = configuration.GetSection(RabbitMqSectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        services.AddRabbitMqEventBus(
            rabbitMqOptions,
            busConfigurator =>
            {
                busConfigurator.AddConsumer<ReserveStockConsumer>();
                busConfigurator.AddConsumer<ConfirmStockReservationConsumer>();
                busConfigurator.AddConsumer<ReleaseStockConsumer>();
            },
            (rabbit, context) => rabbit.UseConsumeFilter(typeof(InboxConsumeFilter<>), context));
        services.AddOutboxProcessor();

        services.AddScoped<IInventoryRepository, InventoryRepository>();

        // IUnitOfWork ve repository AYNI InventoryDbContext instance'ını paylaşır (aynı scope).
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        // Hangfire job'u: scoped → her execution yeni scope açar, repository + UnitOfWork inject eder.
        services.AddScoped<ExpireStockReservationsJob>();
        services.AddHostedService<RecurringJobRegistrar>();

        return services;
    }

    private static void ConfigureOutboxMaps(OutboxEventRegistryBuilder registry)
    {
        registry.Map<StockReserved>(domainEvent => new StockReservedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            ProductId = domainEvent.ProductId,
            Quantity = domainEvent.Quantity,
        });

        registry.Map<StockReservationFailed>(domainEvent => new StockReservationFailedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            ProductId = domainEvent.ProductId,
            Reason = domainEvent.Reason,
        });

        registry.Map<StockReleased>(domainEvent => new StockReleasedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            ProductId = domainEvent.ProductId,
            Quantity = domainEvent.Quantity,
        });

        registry.Map<StockReservationExpired>(domainEvent => new StockReservationExpiredIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            ProductId = domainEvent.ProductId,
            Quantity = domainEvent.Quantity,
        });
    }
}
