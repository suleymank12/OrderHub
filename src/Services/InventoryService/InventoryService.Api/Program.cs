using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.InventoryService.Api.Extensions;
using OrderHub.InventoryService.Application;
using OrderHub.InventoryService.Infrastructure;
using OrderHub.InventoryService.Infrastructure.Persistence;
using OrderHub.InventoryService.Api.BackgroundJobs;
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

    // 5c: Application (handler'lar) + Infrastructure (DbContext + RabbitMQ consumer'lar + outbox + recurring registrar)
    // + Hangfire (expiry job server). HTTP endpoint YOK (command-driven; saga RabbitMQ ile) → JWT/Swagger yok.
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddHangfireServices(builder.Configuration, builder.Environment)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    // ADR-0001 (Seçenek B): yalnızca Development'ta startup migration → "docker-compose up" tek komut.
    // ★ build()'den HEMEN SONRA — Hangfire server/RecurringJobRegistrar (hosted service'ler, app.Run'da başlar)
    // JobStorage'a erişmeden ÖNCE DB var olmalı. Fresh-DB cold-start'ta migration, Hangfire schema-prep'inden
    // (PrepareSchemaIfNecessary) önce DB'yi oluşturur → 'HangFire.Hash' yokluğu (Faz 3 cold-start dersi) engellenir.
    // Production'da otomatik migration YOK (§9 prod-safe); patlarsa app ayağa kalkmaz (fail-fast → outer catch).
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
    Log.Fatal(exception, "InventoryService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
