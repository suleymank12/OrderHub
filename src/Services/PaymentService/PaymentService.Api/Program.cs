using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.PaymentService.Api.Extensions;
using OrderHub.PaymentService.Application;
using OrderHub.PaymentService.Application.Payments.Configuration;
using OrderHub.PaymentService.Infrastructure;
using OrderHub.PaymentService.Infrastructure.Persistence;
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

    // Mock ödeme sağlayıcısı başarı oranı (composition root'ta bind). Geçersiz aralık → startup'ta fail-fast.
    builder.Services.AddOptions<MockPaymentOptions>()
        .Bind(builder.Configuration.GetSection(MockPaymentOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddControllers();

    builder.Services.AddObservability("paymentservice", builder.Configuration);

    var app = builder.Build();

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

    app.MapControllers();

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
    Log.Fatal(exception, "PaymentService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
