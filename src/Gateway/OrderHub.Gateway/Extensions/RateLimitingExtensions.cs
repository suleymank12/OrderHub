using System.Globalization;
using System.Threading.RateLimiting;

namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway rate limiting (ASP.NET Core 8 native — ekstra paket yok). <b>Fixed-window</b>, <b>per-client</b>
/// partition: kimliği doğrulanmış istekte JWT <c>sub</c> claim'ine, aksi halde client IP'sine göre → her client
/// bağımsız kotaya sahip. Config: <c>RateLimiting:PermitLimit</c> / <c>RateLimiting:Window</c> (dev default 100 / 10sn).
/// Aşımda <b>429</b> + <c>Retry-After</c> (client'ın kaç saniye bekleyeceği).
/// </summary>
internal static class RateLimitingExtensions
{
    private const string SectionName = "RateLimiting";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue<int?>($"{SectionName}:PermitLimit") ?? 100;
        var window = configuration.GetValue<TimeSpan?>($"{SectionName}:Window") ?? TimeSpan.FromSeconds(10);

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // ★ Partition anahtarı: doğrulanmış istekte sub (auth middleware'i User'ı doldurdu), yoksa IP.
                var partitionKey = context.User.FindFirst("sub")?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0, // kuyruk yok → limit aşımı anında reddedilir (429).
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                // Fixed-window: client pencere süresi kadar bekleyip yeniden dener.
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });

        return services;
    }
}
