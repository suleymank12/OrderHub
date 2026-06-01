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

                // Consume-pipe filter'ları (inbox dedup vb.) endpoint'lerden ÖNCE → tüm consumer'lara uygulanır.
                configureConsumePipe?.Invoke(rabbit, context);

                // Kayıtlı consumer endpoint'lerini convention ile bağlar (sade topology).
                rabbit.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();

        return services;
    }
}
