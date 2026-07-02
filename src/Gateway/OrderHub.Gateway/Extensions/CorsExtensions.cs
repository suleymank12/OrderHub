namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway CORS — tek merkezi nokta. <c>Cors:AllowedOrigins</c> boşsa dev-loose (AllowAnyOrigin); dolu ise yalnız
/// verilen origin'ler (prod'da kısıtlanır). Header/method her zaman serbest (API tüketimi).
/// </summary>
internal static class CorsExtensions
{
    public const string PolicyName = "GatewayCors";
    private const string OriginsKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(OriginsKey).Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod(); // dev-loose: origin listesi verilmedi.
            }
            else
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod(); // prod: yalnız izinli origin'ler.
            }
        }));

        return services;
    }
}
