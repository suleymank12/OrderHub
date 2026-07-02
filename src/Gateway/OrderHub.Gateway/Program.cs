using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.Gateway.Extensions;
using Serilog;

// Two-stage Serilog init: bootstrap logger DI/startup hatalarını da yakalar.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // ★ SAF EDGE: JWT merkezi doğrulama (downstream'le aynı secret) + per-client rate-limit + CORS + YARP proxy.
    // Contracts/servis bağımlılığı YOK; stateless (DB/migration yok).
    builder.Services
        .AddGatewayAuthentication(builder.Configuration)
        .AddGatewayRateLimiting(builder.Configuration)
        .AddGatewayCors(builder.Configuration);

    builder.Services
        .AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    builder.Services.AddHealthChecks(); // gateway liveness (stateless → downstream dependency check'i YOK).

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseCors(CorsExtensions.PolicyName);

    // ★ Sıra: auth (User doldur) → rate-limit (partition sub/IP) → authorization (route policy enforce).
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    // Liveness: gateway ayakta mı (downstream'e bakmaz → downstream blip'i gateway'i unhealthy yapmasın).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

    // Tüm /api/* trafiği YARP ile downstream'e; route AuthorizationPolicy'si (default=auth / anonymous=dev-token) enforce edilir.
    app.MapReverseProxy();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Gateway terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
