using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderHub.Gateway.Observability;
using Serilog.Core;

namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway distributed tracing (Faz 6 6c-1, §6.3) — ★ pure-edge <b>local wiring</b> (OrderHub.Observability building
/// block'a ProjectRef YOK; gateway hiçbir servis/altyapı projesine bağlanmaz). Building block'un ALT KÜMESİ:
/// AspNetCore (gelen istek span) + Http (YARP forwarder'ın downstream çağrısı → client span + <b>W3C traceparent
/// otomatik inject</b>, downstream trace'e bağlanır) + OTLP→Seq. <b>SqlClient/MassTransit YOK</b> (gateway'de DB/
/// message-bus yok). Serilog trace-id enrichment (<see cref="TraceContextEnricher"/>).
/// </summary>
internal static class GatewayObservabilityExtensions
{
    private const string ServiceName = "gateway";
    private const string OtlpEndpointKey = "Otlp:Endpoint";

    public static IServiceCollection AddGatewayObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // .ReadFrom.Services() bu enricher'ı otomatik alır → gateway logları TraceId/SpanId taşır.
        services.AddSingleton<ILogEventEnricher, TraceContextEnricher>();

        var otlpEndpoint = configuration[OtlpEndpointKey];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()   // gelen istek (edge server span)
                    .AddHttpClientInstrumentation();  // YARP → downstream (client span + traceparent inject)

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });

        return services;
    }
}
