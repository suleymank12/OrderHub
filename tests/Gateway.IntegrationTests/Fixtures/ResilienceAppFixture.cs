using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OrderHub.Gateway.IntegrationTests.Fixtures;

/// <summary>
/// Gateway'i in-process host'layan, <b>resilience testlerine özel</b> factory (Faz 6 6b-1). Kendi WireMock
/// downstream'i + <b>deterministik</b> circuit-breaker config'i (kısa attempt-timeout, düşük eşik, uzun break →
/// timing'siz gözlem). ★ Rota/cluster adları GatewayAppFixture'dakinden AYRI (<c>res-a..res-d</c>) ve env var'lar
/// ADDITIVE → paylaşılan fixture ile ÇAKIŞMAZ; ayrı factory → per-cluster CB state izole. Rotalar <b>anonymous</b>
/// (token gerekmez; resilience forward'da, auth'tan sonra çalışır).
/// </summary>
public sealed class ResilienceAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string Secret = "gateway-resilience-test-secret-key-0123456789"; // ≥32 char (auth ValidateOnStart).
    internal const int MinimumThroughput = 4;   // CB bu kadar çağrı + %50 fail sonrası OPEN.
    internal const int AttemptTimeoutSeconds = 2;

    /// <summary>Testlerin per-path stub/gecikme kurup istek sayısını okuduğu downstream.</summary>
    internal WireMockServer Downstream { get; private set; } = null!;

    internal static readonly string[] Clusters = ["res-a", "res-b", "res-c", "res-d"];

    private readonly List<string> _envKeys = [];

    public Task InitializeAsync()
    {
        Downstream = WireMockServer.Start();
        var url = Downstream.Url!;

        Set("JwtSettings__Secret", Secret);
        Set("RateLimiting__PermitLimit", "1000"); // CB testleri çok istek atar → rate-limit karışmasın.

        // Deterministik CB: uzun sampling (pencere test sırasında dolmaz) + uzun break (half-open test sırasında olmaz).
        Set("Resilience__AttemptTimeoutSeconds", AttemptTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        Set("Resilience__TotalTimeoutSeconds", "10");
        Set("Resilience__CircuitBreaker__FailureRatio", "0.5");
        Set("Resilience__CircuitBreaker__MinimumThroughput", MinimumThroughput.ToString(CultureInfo.InvariantCulture));
        Set("Resilience__CircuitBreaker__SamplingDurationSeconds", "300");
        Set("Resilience__CircuitBreaker__BreakDurationSeconds", "30");

        // Her cluster ayrı circuit → 4 bağımsız devre; hepsi aynı WireMock'a (per-path stub'larla ayrışır).
        foreach (var cluster in Clusters)
        {
            Set($"ReverseProxy__Routes__{cluster}__ClusterId", cluster);
            Set($"ReverseProxy__Routes__{cluster}__AuthorizationPolicy", "anonymous");
            Set($"ReverseProxy__Routes__{cluster}__Match__Path", $"/{cluster}/{{**catch-all}}");
            Set($"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address", url);
        }

        _ = Services; // host'u başlat (config env'den okunur).
        return Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(Environments.Development);

    /// <summary>Verilen cluster path'ini sabit status ile yanıtlar (ör. 500 → transient fail).</summary>
    internal void StubStatus(string cluster, int statusCode) =>
        Downstream
            .Given(Request.Create().WithPath($"/{cluster}/*").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody($"{cluster}:{statusCode}"));

    /// <summary>Verilen cluster path'ini gecikmeli 200 ile yanıtlar (AttemptTimeout'u aşacak → timeout tetikler).</summary>
    internal void StubSlow(string cluster, TimeSpan delay) =>
        Downstream
            .Given(Request.Create().WithPath($"/{cluster}/*").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(delay).WithBody("slow-ok"));

    /// <summary>Bir cluster path'ine downstream'e ULAŞAN (WireMock'a düşen) istek sayısı — CB kanıtının çapası.</summary>
    internal int DownstreamHits(string cluster) =>
        Downstream.FindLogEntries(Request.Create().WithPath($"/{cluster}/*")).Count();

    private void Set(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _envKeys.Add(key);
    }

    public new async Task DisposeAsync()
    {
        foreach (var key in _envKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        Downstream.Stop();
        await base.DisposeAsync();
    }
}
