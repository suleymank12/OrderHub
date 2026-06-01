using System.Net;
using Hangfire.Dashboard;

namespace OrderHub.OrderService.Api.BackgroundJobs;

/// <summary>
/// Hangfire dashboard yetkilendirme filtresi: yalnızca loopback (localhost) bağlantılarına izin verir.
/// <c>X-Forwarded-For</c> gibi proxy header'larına <b>güvenilmez</b> (spoofing riski) — yalnızca ham
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/> değerlendirilir. Bu adres
/// <c>null</c> ise erişim reddedilir (fail-closed, K3). Dashboard zaten yalnızca
/// Development'ta map'lendiği için (bkz. <see cref="HangfireExtensions.MapHangfireDashboardDevOnly"/>) bu
/// filtre ikinci savunma katmanıdır (defense in depth).
/// </summary>
internal sealed class LocalhostDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        IsLoopback(context.GetHttpContext().Connection.RemoteIpAddress);

    /// <summary>
    /// Verilen adres loopback (127.0.0.1 / ::1) mı? <c>null</c> → <c>false</c> (fail-closed). Saf ve Hangfire
    /// bağımlılığı içermez → birim testten doğrudan doğrulanır (proxy/TestServer simülasyonuna gerek yok).
    /// </summary>
    internal static bool IsLoopback(IPAddress? remoteIpAddress) =>
        remoteIpAddress is not null && IPAddress.IsLoopback(remoteIpAddress);
}
