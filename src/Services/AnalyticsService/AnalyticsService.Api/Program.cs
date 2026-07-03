using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.AnalyticsService.Api.Extensions;
using OrderHub.AnalyticsService.Application;
using OrderHub.AnalyticsService.Infrastructure;
using OrderHub.AnalyticsService.Infrastructure.Persistence;
using OrderHub.Observability;
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

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddJwtAuthentication(builder.Configuration)
        .AddApiServices(builder.Configuration)
        .AddSwaggerWithJwt();

    builder.Services.AddControllers();

    builder.Services.AddObservability("analyticsservice", builder.Configuration);

    var app = builder.Build();

    // ADR-0001 (Seçenek B): yalnız Development'ta startup migration → "docker-compose up" tek komut. build()'den
    // HEMEN SONRA (OrderService'in düzeltilmiş pattern'iyle tutarlı; Hangfire yok ama erken-migration standart/güvenli).
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Applying database migrations (Development)...");
        await app.Services.ApplyMigrationsAsync();
    }

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors(ApiServicesExtensions.DevelopmentCorsPolicy);
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Liveness: app ayakta mı (dependency check'i YOK). Readiness: "ready" tag'li check'ler (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    // Read-only analytics endpoint'leri (AnalyticsController): GET orders/{id}, GET revenue/daily.
    app.MapControllers();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "AnalyticsService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
