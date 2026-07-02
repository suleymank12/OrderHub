using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.Inbox;
using OrderHub.Inbox.Consuming;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Abstractions.Scheduling;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Infrastructure.BackgroundJobs;
using OrderHub.OrderService.Infrastructure.Messaging;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence.Interceptors;
using OrderHub.OrderService.Infrastructure.Persistence.Repositories;
using OrderHub.OrderService.Infrastructure.Scheduling;
using OrderHub.Outbox;
using OrderHub.Outbox.Translation;
using OrderHub.EventBus.RabbitMq;

namespace OrderHub.OrderService.Infrastructure;

/// <summary>
/// Infrastructure katmanının DI kayıtları: <see cref="OrderDbContext"/>, repository ve UnitOfWork.
/// Connection string yalnızca burada (Application'da değil) configuration'dan okunur; hard-code edilmez (K3).
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string RabbitMqSectionName = "RabbitMq";
    private const string KafkaBootstrapServersKey = "Kafka:BootstrapServers";
    private const string DefaultKafkaBootstrapServers = "localhost:9092"; // local/test default; compose env override.

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

        // Outbox WRITE yolu (ADR-0002/0006): domain olay → integration olay çevirileri (saf, ekstra DB okuması yok;
        // Id = EventId uçtan uca dedup, Karar 4). OrderConfirmed 1:N fan-out eder (RabbitMQ command + Kafka event);
        // OrderCreated/Paid/Cancelled Faz 4'te Kafka'ya. Money sızdırılmaz → Amount/Currency primitive.
        services.AddOutboxWriter(ConfigureOutboxMaps);

        // Domain event dispatcher: scoped IPublisher (MediatR) bağımlılığı → scoped interceptor (ADR-0002).
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<OrderDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString)
                // Outbox (pre-commit, SavingChanges) ÖNCE; dispatch (post-commit, SavedChanges) sonra.
                // Aşama-bazlı oldukları için yarışmazlar (ADR-0002 Faz 3 Karar 1-2); sıra netlik içindir.
                // Outbox domain event'i yalnız OKUR; tek temizleyici post-commit dispatcher'dır (Karar 2).
                .AddOutboxInterceptor(serviceProvider)
                .AddInterceptors(serviceProvider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        // Outbox processor + inbox dedup port'ları → OrderDbContext (internal olduğundan kayıt Infrastructure'da).
        // İkisi de AYNI scope'ta aynı OrderDbContext instance'ı → inbox filter Add eder, handler aynı instance'ta
        // SaveChanges yapar → atomik (ADR-0005 Karar 3). IOutboxDbContext pattern'iyle birebir.
        services.AddScoped<IOutboxDbContext>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());
        services.AddScoped<IInboxDbContext>(serviceProvider => serviceProvider.GetRequiredService<OrderDbContext>());
        services.AddInbox();

        // Transport (RabbitMQ, ADR-0004) + saga komut consumer'ları + inbox dedup filter + outbox processor.
        // Faz 5 5d-5b cutover: PaymentSucceeded/Failed'ı artık SAGA tüketir (OrderProcessingService) → OrderService
        // consumer'ları KALDIRILDI (çift işleme yok). Saga, stok/ödeme orkestrasyonu sonucu OrderService'e ConfirmOrder/
        // MarkOrderPaid/ShipOrder komutları gönderir; bu consumer'lar onları handler'lara köprüler. Inbox filter
        // (consume-pipe) duplicate'leri consumer'dan önce keser (ADR-0005). RabbitMQ ayarları config'ten (K3).
        var rabbitMqOptions = configuration.GetSection(RabbitMqSectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        services.AddRabbitMqEventBus(
            rabbitMqOptions,
            busConfigurator =>
            {
                // Saga → OrderService komut consumer'ları (AKTİF — OrderCreated→OrderPlaced map'i saga'yı tetikler).
                // ConfirmOrder/Ship idempotent ack; MarkOrderPaid Pending'de throw → retry (Karar D).
                busConfigurator.AddConsumer<ConfirmOrderConsumer>();
                busConfigurator.AddConsumer<MarkOrderPaidConsumer>();
                busConfigurator.AddConsumer<ShipOrderConsumer>();
                // Faz 5e-1 (compensation altyapısı): iptal komutu consumer'ı. ★ DORMANT — saga CancelOrder'ı
                // 5e-2'de gönderecek; şimdilik kayıtlı ama tetiklenmiyor (additive/dormant, çalışan akış değişmez).
                busConfigurator.AddConsumer<CancelOrderConsumer>();
            },
            (rabbit, context) => rabbit.UseConsumeFilter(typeof(InboxConsumeFilter<>), context));

        // Kafka event-stream routing (ADR-0006): AddRabbitMqEventBus'tan SONRA → processor'ın gördüğü
        // IIntegrationEventPublisher routing publisher'a evrilir (IKafkaEvent → Kafka, aksi → RabbitMQ).
        // Bootstrap servers config'ten (compose'da env override, K3 kapsamı dışı ama ortama göre değişir).
        var kafkaBootstrapServers = configuration[KafkaBootstrapServersKey] ?? DefaultKafkaBootstrapServers;
        services.AddKafkaRouting(kafkaBootstrapServers);

        services.AddOutboxProcessor();

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

    // Domain olay → integration olay outbox çevirileri (saf factory'ler; Id = EventId, ADR-0002 Karar 4 invariant
    // registry tarafından runtime doğrulanır). Kayıt sırası = Ordinal (0,1,…). Faz 5 5d-5b cutover:
    // - OrderCreated 1:N: OrderCreatedIntegrationEvent (Kafka/Analytics, Ordinal 0) + OrderPlacedIntegrationEvent
    //   (RabbitMQ, saga trigger, Ordinal 1). Saga'yı bu tetikler.
    // - OrderConfirmed artık 1:1: yalnız OrderConfirmedIntegrationEvent (Kafka/Analytics). ProcessPayment map'i
    //   KALDIRILDI — ödemeyi artık SAGA publish eder (çift ProcessPayment yok).
    private static void ConfigureOutboxMaps(OutboxEventRegistryBuilder registry)
    {
        registry.Map<OrderConfirmed>(domainEvent => new OrderConfirmedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            Amount = domainEvent.Total.Amount,
            Currency = domainEvent.Total.Currency.ToString(),
        });

        registry.Map<OrderCreated>(domainEvent => new OrderCreatedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            Amount = domainEvent.Total.Amount,
            Currency = domainEvent.Total.Currency.ToString(),
        });

        // Ordinal 1: saga trigger (RabbitMQ). Kalemler OrderCreatedItem → OrderPlacedItem; CustomerId saga'nın
        // ProcessPayment'ı için, Amount/Currency Total'dan (primitive — Money sızdırılmaz).
        registry.Map<OrderCreated>(domainEvent => new OrderPlacedIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            Amount = domainEvent.Total.Amount,
            Currency = domainEvent.Total.Currency.ToString(),
            Items = domainEvent.Items
                .Select(item => new OrderPlacedItem(item.ProductId, item.Quantity))
                .ToList(),
        });

        registry.Map<OrderPaid>(domainEvent => new OrderPaidIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
        });

        registry.Map<OrderCancelled>(domainEvent => new OrderCancelledIntegrationEvent
        {
            Id = domainEvent.EventId,
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            OrderId = domainEvent.OrderId,
            Reason = domainEvent.Reason,
        });
    }
}
