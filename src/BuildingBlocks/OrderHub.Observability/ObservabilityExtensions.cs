using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog.Core;

namespace OrderHub.Observability;

/// <summary>
/// OpenTelemetry distributed tracing DI kaydı (Faz 6 6c, §6.3). <b>Yalnız tracing</b> (metrics ayrı faz).
/// <b>Otomatik</b> instrumentation: gelen/giden HTTP (W3C traceparent otomatik propagate), SqlClient (DB span),
/// MassTransit (RabbitMQ hop — native ActivitySource). Kafka hop'u custom producer/consumer olduğundan MANUEL
/// propagation gerektirir (6c-2). Traces OTLP ile Seq'e; loglar <see cref="TraceContextEnricher"/> ile trace-id taşır.
/// </summary>
public static class ObservabilityExtensions
{
    private const string OtlpEndpointKey = "Otlp:Endpoint";
    private const string MassTransitActivitySource = "MassTransit";

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(configuration);

        // Serilog trace-id enrichment — .ReadFrom.Services() bu ILogEventEnricher'ı otomatik alır (Program.cs değişmez).
        services.AddSingleton<ILogEventEnricher, TraceContextEnricher>();

        var otlpEndpoint = configuration[OtlpEndpointKey];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName)) // service.name → Seq'te servise göre ayrışır
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(serviceName)                 // servisin kendi custom span'leri (6c-2 Kafka producer)
                    .AddSource(MassTransitActivitySource)   // MassTransit native → RabbitMQ hop otomatik trace
                    .AddAspNetCoreInstrumentation()         // gelen HTTP (server span)
                    .AddHttpClientInstrumentation()         // giden HTTP (client span + traceparent inject)
                    .AddSqlClientInstrumentation();         // DB span (EF Core → SqlClient; STABLE, EF beta değil)

                // OTLP → Seq (config-driven). Endpoint yoksa (test/local) exporter EKLENMEZ → startup patlamaz.
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OtlpExportProtocol.HttpProtobuf; // Seq OTLP/HTTP ingest
                    });
                }
            });

        return services;
    }
}
