using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.EventBus.RabbitMq;

/// <summary>
/// MassTransit + RabbitMQ taşıma katmanını ve <see cref="IIntegrationEventPublisher"/>'ı kaydeden DI
/// uzantısı (ADR-0004). Consumer/saga kayıtları servis composition root'undan <c>configureBus</c> geri
/// çağrısı ile eklenir (DIP); bu building block hangi consumer'ların var olduğunu bilmez, yalnızca transport'u kurar.
/// </summary>
public static class EventBusServiceCollectionExtensions
{
    /// <summary>
    /// RabbitMQ üzerinde MassTransit bus'ını ve integration event publisher'ını kaydeder. Consumer/saga
    /// kayıtları <paramref name="configureBus"/> ile; consume-pipe filter'ları (ör. inbox dedup)
    /// <paramref name="configureConsumePipe"/> ile — <c>ConfigureEndpoints</c>'ten ÖNCE uygulanır ki tüm
    /// endpoint'lere bağlansın. Building block hangi filter/consumer olduğunu bilmez (DIP).
    /// </summary>
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        RabbitMqOptions options,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<IRabbitMqBusFactoryConfigurator, IBusRegistrationContext>? configureConsumePipe = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddMassTransit(busConfigurator =>
        {
            // Consumer/saga kayıtları (varsa) buradan; sıra önemli: endpoint convention kayıtlı consumer'ları görür.
            configureBus?.Invoke(busConfigurator);

            busConfigurator.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(options.Host, options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                });

                // ★ Retry EN DIŞTA (ROADMAP §3.5): consume-pipe spec'leri ekleme sırasıyla uygulanır, ilk eklenen
                // en dış. Retry'ı inbox filter'dan ÖNCE eklemek → retry → [her denemede] inbox filter → consumer.
                // Transient hata → tüm consume (inbox tracked-Add dahil) SaveChanges'siz rollback → satır yok →
                // retry temiz tekrar dener; başarı → atomik commit → satır var → redelivery skip (ADR-0005 Karar 4).
                // Tükenince mesaj MassTransit convention'ı ile <queue>_error queue'ya (DLQ) düşer — ekstra kod yok.
                var retry = options.Retry;
                rabbit.UseMessageRetry(r => r.Exponential(
                    retry.RetryLimit, retry.MinInterval, retry.MaxInterval, retry.IntervalDelta));

                // Consume-pipe filter'ları (inbox dedup vb.) retry'dan SONRA, endpoint'lerden ÖNCE → retry bunları sarar.
                configureConsumePipe?.Invoke(rabbit, context);

                // Kayıtlı consumer endpoint'lerini convention ile bağlar (sade topology).
                rabbit.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();

        return services;
    }
}
