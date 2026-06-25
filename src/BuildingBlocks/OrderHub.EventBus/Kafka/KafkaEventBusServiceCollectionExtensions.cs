using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.EventBus.RabbitMq;

namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Kafka event-stream'i publish yoluna ekleyen DI uzantısı (ADR-0006). <b>AddRabbitMqEventBus'tan SONRA</b>
/// çağrılır: mevcut <see cref="MassTransitIntegrationEventPublisher"/>'ı RabbitMQ-keyed, yeni
/// <see cref="KafkaIntegrationEventPublisher"/>'ı Kafka-keyed kaydeder ve processor'ın gördüğü
/// <see cref="IIntegrationEventPublisher"/>'ı <see cref="RoutingIntegrationEventPublisher"/>'a override eder.
/// Producer/consumer wiring servis composition root'undadır (bu building block transport'u kurar, event'leri bilmez).
/// </summary>
public static class KafkaEventBusServiceCollectionExtensions
{
    /// <summary>
    /// Kafka producer'ı + marker-routing publisher'ı kaydeder. <paramref name="bootstrapServers"/> Kafka broker
    /// adres(ler)i (config'ten; secret değil, K3 kapsamı dışı ama ortama göre değişir).
    /// </summary>
    public static IServiceCollection AddKafkaRouting(this IServiceCollection services, string bootstrapServers)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);

        // IProducer singleton (thread-safe, idempotent). Lazy bağlanır (ilk Produce'da) → broker yokken kayıt güvenli.
        services.AddSingleton<IProducer<string, string>>(
            _ => new ProducerBuilder<string, string>(BuildProducerConfig(bootstrapServers)).Build());

        // RabbitMQ (mevcut MassTransit) + Kafka publisher'ları keyed; routing = görünür IIntegrationEventPublisher (last-wins).
        services.AddKeyedScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>(
            RoutingIntegrationEventPublisher.RabbitMqKey);
        services.AddKeyedScoped<IIntegrationEventPublisher, KafkaIntegrationEventPublisher>(
            RoutingIntegrationEventPublisher.KafkaKey);
        services.AddScoped<IIntegrationEventPublisher, RoutingIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Idempotent producer config (ADR-0006 Karar 2 / ROADMAP §4.3): <c>Acks=All</c> + <c>EnableIdempotence=true</c>
    /// → broker tarafı duplicate'leri eler, tüm replica ack'leri beklenir (üretimde en güçlü güvence).
    /// </summary>
    internal static ProducerConfig BuildProducerConfig(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
    };
}
