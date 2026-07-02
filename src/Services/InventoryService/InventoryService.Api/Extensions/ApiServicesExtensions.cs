namespace OrderHub.InventoryService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları. 5b iskeleti: yalnız health check (DB readiness). HTTP endpoint
/// olmadığından (command-driven) ProblemDetails/exception-handler/CORS/JWT EKLENMEZ (YAGNI) — bunlar 5c/okuma-API
/// gelirse eklenir.
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
