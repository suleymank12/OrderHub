using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.OrderService.Api.BackgroundJobs;
using OrderHub.OrderService.Api.Contracts;
using OrderHub.OrderService.Api.Extensions;
using OrderHub.OrderService.Api.Identity;
using OrderHub.OrderService.Application.Orders.Configuration;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Persistence;
using Serilog;

// Two-stage Serilog init: bootstrap logger configuration okunmadan önceki (DI/startup) hatalarını da yakalar.
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
        .AddHangfireServices(builder.Configuration, builder.Environment)
        .AddJwtAuthentication(builder.Configuration)
        .AddApiServices(builder.Configuration)
        .AddSwaggerWithJwt();

    // Ödenmeyen sipariş zaman aşımı politikası (Application option'ı, composition root'ta bind edilir).
    // Geçersiz süre → startup'ta fail-fast (cryptic runtime hatası yerine net).
    builder.Services.AddOptions<OrderTimeoutOptions>()
        .Bind(builder.Configuration.GetSection(OrderTimeoutOptions.SectionName))
        .Validate(options => options.UnpaidTimeout > TimeSpan.Zero, "OrderTimeout:UnpaidTimeout must be positive.")
        .ValidateOnStart();

    builder.Services.AddControllers();

    var app = builder.Build();

    // ExceptionHandler en dışta → tüm downstream exception'lar ProblemDetails'e çevrilir.
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging(); // request log + TraceId/RequestId enrich.

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors(ApiServicesExtensions.DevelopmentCorsPolicy);
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Hangfire dashboard yalnızca Development'ta map'lenir (prod'da endpoint yok). İçeride localhost filtresi var.
    app.MapHangfireDashboardDevOnly();

    // Liveness: app ayakta mı (dependency check'i YOK). Readiness: "ready" tag'li check'ler (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    // Development-only token endpoint: gerçek login/identity provider kapsam dışı. Prod'da bu kod yolu
    // hiç çalışmaz (pipeline'a eklenmez) → attack surface yok.
    if (app.Environment.IsDevelopment())
    {
        app.MapPost("/api/dev/token", (TokenRequest request, ITokenGenerator tokenGenerator) =>
            {
                var (token, expiresAtUtc) = tokenGenerator.GenerateToken(request.CustomerId ?? Guid.NewGuid());
                return Results.Ok(new TokenResponse(token, expiresAtUtc));
            })
            .AllowAnonymous()
            .WithTags("Dev");
    }

    // ADR-0001 (Seçenek B): yalnızca Development'ta startup'ta migration uygula → "docker-compose up"
    // tek komutla çalışsın. Production'da otomatik migration YOK (§9 prod-safe). Migration patlarsa
    // app ayağa kalkmaz (fail-fast → outer catch → container exit; şema yoksa zaten çalışamaz).
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Applying database migrations (Development)...");
        await app.Services.ApplyMigrationsAsync();
    }

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "OrderService API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testlerin (WebApplicationFactory) erişebilmesi için partial Program sınıfı.</summary>
public partial class Program;
