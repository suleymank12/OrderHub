namespace OrderHub.OrderProcessingService.Api.Extensions;

/// <summary>
/// Api-seviyesi cross-cutting servis kayıtları. Saga host iskeleti (5d-4a): yalnız health check (DB readiness).
/// HTTP endpoint olmadığından (saga RabbitMQ ile orkestre eder) ProblemDetails/exception-handler/CORS/JWT
/// EKLENMEZ (YAGNI). InventoryService precedent'iyle aynı.
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
