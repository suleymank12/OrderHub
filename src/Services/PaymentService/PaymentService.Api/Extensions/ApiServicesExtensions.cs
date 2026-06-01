using OrderHub.PaymentService.Api.ExceptionHandling;

namespace OrderHub.PaymentService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları: ProblemDetails, global exception handler, health check'ler
/// ve (dev) CORS. PaymentService'in current-user/token-generator ihtiyacı yok (ödeme event-driven tetiklenir).
/// </summary>
internal static class ApiServicesExtensions
{
    /// <summary>Yalnızca Development'ta uygulanan gevşek CORS politikası adı.</summary>
    public const string DevelopmentCorsPolicy = "DevelopmentCors";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // RFC 7807 ProblemDetails + global exception → ProblemDetails çevirici.
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // /health/ready için DB check (SELECT 1). Connection string eksikse check unhealthy döner (startup'ı çökertmez).
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sql-server", tags: ["ready"]);

        // Dev'de tarayıcı/Swagger kolaylığı; prod'da pipeline'a EKLENMEZ (Program guard).
        services.AddCors(options => options.AddPolicy(
            DevelopmentCorsPolicy,
            policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        return services;
    }
}
