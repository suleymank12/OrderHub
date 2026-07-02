namespace OrderHub.NotificationService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları. 5f-1 iskeleti: yalnız health check (DB readiness).
/// HTTP endpoint olmadığından (Kafka consumer-only) ProblemDetails/exception-handler/CORS/JWT/Swagger
/// EKLENMEZ (YAGNI) — bunlar 5f-2 (e-posta API) gelirse değerlendirilir.
/// </summary>
internal static class ApiServicesExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // /health/ready için DB check (SELECT 1). Connection string eksikse check unhealthy döner (startup'ı çökertmez).
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sql-server", tags: ["ready"]);

        return services;
    }
}
