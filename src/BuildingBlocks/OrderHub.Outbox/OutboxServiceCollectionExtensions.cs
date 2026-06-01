using Microsoft.Extensions.DependencyInjection;
using OrderHub.Outbox.Interceptors;
using OrderHub.Outbox.Processing;
using OrderHub.Outbox.Translation;

namespace OrderHub.Outbox;

/// <summary>
/// Outbox altyapısının DI uzantıları. Yazma yolu (<see cref="AddOutboxWriter"/>) ve işleme yolu
/// (<see cref="AddOutboxProcessor"/>) <b>ayrı</b> kayıtlardır: bir servis outbox'a yazıp (atomik,
/// pre-commit) publish'i (RabbitMQ) sonraki faza erteleyebilir. Faz 3 Adım 2 yalnızca writer'ı devreye
/// alır; processor + transport 3.6'da eklenir.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Outbox <b>yazma</b> yolunu kaydeder: çeviri registry'si + pre-commit <see cref="OutboxInterceptor"/>.
    /// Interceptor burada yalnızca DI'a kaydedilir; servisin <c>DbContext</c> options'ına
    /// (<c>AddInterceptors</c>) eklenmesi ve <see cref="IOutboxDbContext"/> implementasyonu servis
    /// Infrastructure'ının işidir. Domain event → outbox satırı aynı transaction'da yazılır; domain event
    /// <b>clear edilmez</b> (tek temizleyici post-commit dispatcher'dır, ADR-0002 Faz 3 Karar 2).
    /// </summary>
    public static IServiceCollection AddOutboxWriter(
        this IServiceCollection services,
        Action<OutboxEventRegistryBuilder> configureRegistry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRegistry);

        var registryBuilder = new OutboxEventRegistryBuilder();
        configureRegistry(registryBuilder);

        // Registry immutable → singleton; interceptor yalnızca registry'ye bağlı → singleton güvenli.
        services.AddSingleton(registryBuilder.Build());
        services.AddSingleton<OutboxInterceptor>();

        return services;
    }

    /// <summary>
    /// Outbox <b>işleme</b> yolunu kaydeder: polling <see cref="OutboxProcessorService"/> (HostedService) +
    /// ayarları. Çalışması için bir <see cref="OrderHub.EventBus.IIntegrationEventPublisher"/> ve
    /// <see cref="IOutboxDbContext"/> kayıtlı olmalıdır (transport kurulumu). Faz 3 Adım 2'de
    /// <b>çağrılmaz</b> (write-only); 3.6'da RabbitMQ ile birlikte devreye alınır.
    /// </summary>
    public static IServiceCollection AddOutboxProcessor(
        this IServiceCollection services,
        Action<OutboxProcessorOptions>? configureProcessor = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // configureProcessor null olsa bile options altyapısı kayıtlanır → IOptions defaults ile resolve olur.
        services.Configure<OutboxProcessorOptions>(processorOptions => configureProcessor?.Invoke(processorOptions));
        services.AddHostedService<OutboxProcessorService>();

        return services;
    }
}
