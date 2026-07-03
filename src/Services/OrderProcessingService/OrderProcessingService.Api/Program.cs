using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.OrderProcessingService.Api.Extensions;
using OrderHub.OrderProcessingService.Infrastructure;
using OrderHub.OrderProcessingService.Infrastructure.Persistence;
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

    // Saga host: Infrastructure (SagasDbContext + MassTransit saga state machine + RabbitMQ) + Api (health).
    // HTTP endpoint YOK (saga RabbitMQ ile orkestre eder) → JWT/Swagger yok. Application/Domain katmanı yok.
    builder.Services
        .AddInfrastructure(builder.Configuration)
        .AddApiServices(builder.Configuration);

    builder.Services.AddObservability("orderprocessingservice", builder.Configuration);

    var app = builder.Build();

    // ADR-0001 (Seçenek B): yalnızca Development'ta startup migration → "docker-compose up" tek komut.
    // ★ build()'den HEMEN SONRA, app.Run()'dan (MassTransit bus hosted service'i burada başlar) ÖNCE: saga
    // endpoint'i state tablosuna (OrderHub_Sagas) erişmeden DB + tablo var olmalı. Production'da otomatik
    // migration YOK (§9 prod-safe); patlarsa app ayağa kalkmaz (fail-fast → outer catch).
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Applying database migrations (Development)...");
        await app.Services.ApplyMigrationsAsync();
    }

    app.UseSerilogRequestLogging();

    // Liveness: app ayakta mı (dependency check'i YOK). Readiness: "ready" tag'li check'ler (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "OrderProcessingService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
