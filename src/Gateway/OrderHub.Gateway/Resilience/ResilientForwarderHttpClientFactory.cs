using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Yarp.ReverseProxy.Forwarder;

namespace OrderHub.Gateway.Resilience;

/// <summary>
/// YARP direct forwarder'ının downstream HTTP çağrılarına Polly resilience ekleyen özel factory. YARP forwarder'ı
/// IHttpClientFactory'nin named-client + DelegatingHandler pipeline'ını DEĞİL <see cref="HttpMessageInvoker"/>
/// kullanır → <c>AddStandardResilienceHandler</c> doğrudan takılamaz. Resilience,
/// <see cref="ForwarderHttpClientFactory.WrapHandler"/> extension noktasında handler zincirine eklenir.
/// <para>
/// Pipeline <b>per-cluster</b>: <see cref="ResiliencePipelineRegistry{TKey}"/> (key = clusterId) → her downstream'in
/// circuit-breaker state'i izole (biri çökünce diğerleri etkilenmez). ★ Saf-edge korunur: yalnız HTTP hop, ProjectRef yok.
/// </para>
/// 6b-1: per-attempt timeout + circuit-breaker (RETRY YOK — 6b-2'de CB ile AttemptTimeout arasına eklenecek).
/// </summary>
internal sealed class ResilientForwarderHttpClientFactory : ForwarderHttpClientFactory, IDisposable
{
    /// <summary>Retry stratejisinin isteğin HTTP metodunu okuduğu context anahtarı (handler set eder).</summary>
    internal static readonly ResiliencePropertyKey<HttpMethod> RequestMethodKey = new("gateway.request.method");

    // ★ ALLOWLIST (denylist DEĞİL): YALNIZ bu güvenli/idempotent metodlar retry edilir. POST/PUT/PATCH/DELETE ve
    // BİLİNMEYEN her method retry EDİLMEZ (güvenlik varsayılanı KAPALI) → yeni method eklenirse otomatik güvenli.
    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    private readonly GatewayResilienceOptions _options;
    private readonly ResiliencePipelineRegistry<string> _registry = new();

    public ResilientForwarderHttpClientFactory(
        IOptions<GatewayResilienceOptions> options,
        ILogger<ForwarderHttpClientFactory> logger)
        : base(logger)
        => _options = options.Value;

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
    {
        var inner = base.WrapHandler(context, handler);
        // Per-cluster: aynı clusterId → aynı pipeline instance (CB state paylaşılır/kalıcı); farklı cluster → izole.
        var pipeline = _registry.GetOrAddPipeline<HttpResponseMessage>(
            context.ClusterId,
            builder => ConfigurePipeline(builder, _options));
        return new ResiliencePipelineDelegatingHandler(pipeline) { InnerHandler = inner };
    }

    private static void ConfigurePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        GatewayResilienceOptions options)
    {
        // Sıra (dış→iç): TotalTimeout → CircuitBreaker → RETRY → AttemptTimeout. Retry her denemeye AttemptTimeout
        // uygular; CB retry'ın NİHAİ sonucunu görür (retry'ın kurtardığı geçici blip CB'yi tetiklemez).
        builder
            .AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds))
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // ShouldHandle default = HttpClientResiliencePredicates.IsTransient (5xx / 408 / HttpRequestException).
                FailureRatio = options.CircuitBreaker.FailureRatio,
                MinimumThroughput = options.CircuitBreaker.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreaker.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreaker.BreakDurationSeconds),
            })
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.RetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true, // DecorrelatedJitter → eşzamanlı retry storm'unu dağıtır
                Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
                // ★ İKİ koşul: (1) method allowlist'te (idempotent) VE (2) outcome transient. İkisi de sağlanmazsa retry YOK.
                ShouldHandle = ShouldRetry,
            })
            .AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
    }

    /// <summary>
    /// ★ Retry karar fonksiyonu — çift-order güvenliğinin 1. katmanı. Method allowlist'te DEĞİLSE (POST/PUT/PATCH/
    /// DELETE/bilinmeyen) transient olsa bile retry EDİLMEZ. Allowlist içiyse yalnız transient (5xx/408/HttpRequestException)
    /// retry edilir (4xx client hatası retry edilmez).
    /// </summary>
    private static ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args)
    {
        if (!args.Context.Properties.TryGetValue(RequestMethodKey, out var method)
            || !IdempotentMethods.Contains(method.Method))
        {
            return PredicateResult.False(); // allowlist dışı → ASLA retry (güvenli varsayılan)
        }

        return HttpClientResiliencePredicates.IsTransient(args.Outcome)
            ? PredicateResult.True()
            : PredicateResult.False();
    }

    public void Dispose() => _registry.Dispose();
}

/// <summary>
/// YARP forwarder handler zincirine takılan ince DelegatingHandler: her isteği verilen per-cluster Polly
/// <see cref="ResiliencePipeline{T}"/>'ı içinde çalıştırır. Circuit OPEN'da pipeline <c>BrokenCircuitException</c>
/// fırlatır → downstream'e HİÇ gidilmez (fast-fail); AttemptTimeout aşımında <c>TimeoutRejectedException</c> → çağrı
/// bounded. YARP bu exception'ları forwarder error'a (502/504) çevirir.
/// </summary>
internal sealed class ResiliencePipelineDelegatingHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public ResiliencePipelineDelegatingHandler(ResiliencePipeline<HttpResponseMessage> pipeline)
        => _pipeline = pipeline;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Retry allowlist'inin okuyabilmesi için isteğin metodunu context'e koy (exception outcome'da response yoktur).
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResilientForwarderHttpClientFactory.RequestMethodKey, request.Method);
        try
        {
            return await _pipeline.ExecuteAsync(
                async ctx => await base.SendAsync(request, ctx.CancellationToken).ConfigureAwait(false),
                context).ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
