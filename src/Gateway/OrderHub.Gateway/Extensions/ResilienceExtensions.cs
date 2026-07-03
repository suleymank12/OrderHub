using OrderHub.Gateway.Resilience;
using Yarp.ReverseProxy.Forwarder;

namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway resilience DI kaydı (Faz 6 6b): <see cref="GatewayResilienceOptions"/> bind + fail-fast doğrulama +
/// YARP forwarder factory'sini <see cref="ResilientForwarderHttpClientFactory"/> ile değiştirir (WrapHandler →
/// per-cluster Polly pipeline). ★ AddReverseProxy'den ÖNCE çağrılır: YARP default factory'sini
/// <c>TryAddSingleton</c> ile kaydeder → bizimki zaten varsa YARP atlar (tek, deterministik kayıt).
/// </summary>
internal static class ResilienceExtensions
{
    public static IServiceCollection AddGatewayResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GatewayResilienceOptions>()
            .Bind(configuration.GetSection(GatewayResilienceOptions.SectionName))
            .ValidateDataAnnotations() // top-level timeout'lar (pozitif) — startup'ta
            .Validate(
                o => o.CircuitBreaker.FailureRatio is > 0 and <= 1
                    && o.CircuitBreaker.MinimumThroughput >= 2
                    && o.CircuitBreaker.SamplingDurationSeconds >= 1
                    && o.CircuitBreaker.BreakDurationSeconds >= 1,
                "Resilience:CircuitBreaker geçersiz (FailureRatio 0-1, MinimumThroughput>=2, süreler>=1sn).")
            .ValidateOnStart(); // eksik/geçersiz config → startup fail-fast (K3/downstream ile tutarlı)

        // ★ YARP forwarder factory override: downstream HTTP hop'a per-cluster timeout+CB (saf-edge, ProjectRef yok).
        services.AddSingleton<IForwarderHttpClientFactory, ResilientForwarderHttpClientFactory>();
        return services;
    }
}
