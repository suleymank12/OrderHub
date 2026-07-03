using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
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
        // Sıra (dış→iç): TotalTimeout → CircuitBreaker → AttemptTimeout. RETRY YOK (6b-1); 6b-2 retry CB↔AttemptTimeout'a girer.
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
            .AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
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
        => await _pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
}
