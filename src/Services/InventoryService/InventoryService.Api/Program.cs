using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.InventoryService.Api.Extensions;
using OrderHub.InventoryService.Infrastructure;
using OrderHub.InventoryService.Infrastructure.Persistence;
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

    // 5b iskeleti: yalnız Infrastructure (DbContext) + health. HTTP endpoint YOK (Inventory command-driven;
    // saga RabbitMQ ile konuşur — consumer 5c). Application (handler'lar) + JWT (read-API yok) 5c'de.
    builder.Services
        .AddInfrastructure(builder.Configuration)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Liveness: app ayakta mı (dependency check'i YOK). Readiness: "ready" tag'li check'ler (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    // ADR-0001 (Seçenek B): yalnızca Development'ta startup migration → "docker-compose up" tek komut.
    // Production'da otomatik migration YOK (§9 prod-safe); patlarsa app ayağa kalkmaz (fail-fast).
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Applying database migrations (Development)...");
        await app.Services.ApplyMigrationsAsync();
    }

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
