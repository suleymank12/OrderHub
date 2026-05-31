using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderHub.OrderService.Api.Contracts;
using OrderHub.OrderService.Api.Extensions;
using OrderHub.OrderService.Api.Identity;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Infrastructure;
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
        .AddJwtAuthentication(builder.Configuration)
        .AddApiServices(builder.Configuration)
        .AddSwaggerWithJwt();

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
