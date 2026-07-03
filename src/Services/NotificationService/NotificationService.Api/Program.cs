using System.Globalization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.NotificationService.Api.BackgroundJobs;
using OrderHub.NotificationService.Api.Extensions;
using OrderHub.NotificationService.Application;
using OrderHub.NotificationService.Infrastructure;
using OrderHub.NotificationService.Infrastructure.Persistence;
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

    // Kafka consumer (order-stream) + DB context + health checks.
    // JWT/Swagger/Controllers YOK: NotificationService bu fazda bildirim read-model consumer'ı; HTTP API yok (K3).
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddHangfireServices(builder.Configuration, builder.Environment)
        .AddApiServices(builder.Configuration);

    builder.Services.AddObservability("notificationservice", builder.Configuration);

    var app = builder.Build();

    // ADR-0001 (Seçenek B): yalnızca Development'ta startup migration → "docker-compose up" tek komut.
    // build()'den HEMEN SONRA — OrderEventsConsumer (hosted service, app.Run'da başlar) DB'ye erişmeden
    // önce şema var olmalı.
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Applying database migrations (Development)...");
        await app.Services.ApplyMigrationsAsync();
    }

    app.UseSerilogRequestLogging();

    // Liveness: app ayakta mı (dependency check'i YOK). Readiness: "ready" tag'li check'ler (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse });

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "NotificationService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
