using OrderHub.AnalyticsService.Api.ExceptionHandling;

namespace OrderHub.AnalyticsService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları: ProblemDetails, global exception handler, health check'ler ve
/// (dev) CORS. 4d: <see cref="GlobalExceptionHandler"/> ValidationBehavior'dan gelen
/// <see cref="FluentValidation.ValidationException"/>'ı 400'e map'ler (ters tarih aralığı vb.).
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

        services.AddCors(options => options.AddPolicy(
            DevelopmentCorsPolicy,
            policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        return services;
    }
}
