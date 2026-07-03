using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Core;

namespace OrderHub.Observability.UnitTests;

/// <summary>
/// Faz 6 6c-1 — <see cref="ObservabilityExtensions.AddObservability"/> wiring testleri. TracerProvider kaydı +
/// serviceName ActivitySource'unun dinlendiği (span üretimi) + Serilog trace enricher kaydı deterministik doğrulanır.
/// OTLP endpoint verilmez → exporter eklenmez (broker'sız, test host'u ayakta).
/// </summary>
public sealed class ObservabilityWiringTests
{
    private const string ServiceName = "test-service";

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().Build(); // Otlp:Endpoint yok → OTLP exporter eklenmez
        var services = new ServiceCollection();
        services.AddObservability(ServiceName, configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddObservability_RegistersTracerProvider()
    {
        using var provider = BuildProvider();

        provider.GetService<TracerProvider>().Should().NotBeNull("OTel SDK TracerProvider DI'a kaydedilmeli");
    }

    [Fact]
    public void AddObservability_ListensToServiceActivitySource_SoSpansAreRecorded()
    {
        using var provider = BuildProvider();
        _ = provider.GetRequiredService<TracerProvider>(); // ★ önce SDK'yı kur → ActivityListener kaydolur

        using var source = new ActivitySource(ServiceName); // AddSource(serviceName) ile eşleşir
        using var activity = source.StartActivity("probe");

        activity.Should().NotBeNull("kayıtlı source dinlendiği için activity RECORDED olmalı (span üretimi kanıtı)");
    }

    [Fact]
    public void AddObservability_RegistersTraceContextEnricher_ForSerilogCorrelation()
    {
        using var provider = BuildProvider();

        var enrichers = provider.GetServices<ILogEventEnricher>();

        enrichers.Should().ContainSingle(e => e is TraceContextEnricher,
            "trace-id/span-id log korelasyonu için TraceContextEnricher ILogEventEnricher olarak kaydedilmeli");
    }
}
