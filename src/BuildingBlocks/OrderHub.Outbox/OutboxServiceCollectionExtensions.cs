using Microsoft.Extensions.DependencyInjection;
using OrderHub.Outbox.Interceptors;
using OrderHub.Outbox.Processing;
using OrderHub.Outbox.Translation;

namespace OrderHub.Outbox;

/// <summary>
/// Outbox altyapısını kaydeden DI uzantısı: çeviri registry'si, pre-commit interceptor ve polling
/// processor. <see cref="OutboxInterceptor"/> burada yalnızca <b>kaydedilir</b>; servisin <c>DbContext</c>
/// options'ına eklenmesi (AddInterceptors) ve <see cref="IOutboxDbContext"/> implementasyonu servis
/// Infrastructure'ının işidir (sonraki adım; bu adımda hiçbir migration üretilmez).
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>Outbox registry + interceptor + processor'ı kaydeder.</summary>
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxEventRegistryBuilder> configureRegistry,
        Action<OutboxProcessorOptions>? configureProcessor = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRegistry);

        var registryBuilder = new OutboxEventRegistryBuilder();
        configureRegistry(registryBuilder);

        // Registry immutable → singleton; interceptor yalnızca registry'ye bağlı → singleton güvenli.
        services.AddSingleton(registryBuilder.Build());
        services.AddSingleton<OutboxInterceptor>();

        // configureProcessor null olsa bile options altyapısı kayıtlanır → IOptions defaults ile resolve olur.
        services.Configure<OutboxProcessorOptions>(processorOptions => configureProcessor?.Invoke(processorOptions));
        services.AddHostedService<OutboxProcessorService>();

        return services;
    }
}
