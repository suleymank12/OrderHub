namespace OrderHub.AnalyticsService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları: ProblemDetails, health check'ler ve (dev) CORS.
/// 4c-1 iskelet: özel <c>IExceptionHandler</c> YOK — <c>UseExceptionHandler()</c> + <c>AddProblemDetails()</c>
/// default ProblemDetails davranışını kullanır (endpoint/validation-özgü mapping query'lerle birlikte 4d'de).
/// </summary>
internal static class ApiServicesExtensions
{
    /// <summary>Yalnızca Development'ta uygulanan gevşek CORS politikası adı.</summary>
    public const string DevelopmentCorsPolicy = "DevelopmentCors";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails(); // RFC 7807 — UseExceptionHandler default handler bunu kullanır.

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
