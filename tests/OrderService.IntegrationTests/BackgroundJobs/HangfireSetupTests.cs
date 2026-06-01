using System.Net;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.BackgroundJobs;

/// <summary>
/// Hangfire DI kaydı ve dashboard davranışı integration testleri.
/// <para>
/// <b>Test 1 — DI kaydı:</b> <see cref="IBackgroundJobClient"/> container'dan resolve edilebilir
/// (AddHangfireServices wire doğrulaması). Host başlatılır (Services erişimi),
/// bu sırada <c>PrepareSchemaIfNecessary=true</c> (Development) HangFire şemasını oluşturur.
/// </para>
/// <para>
/// <b>Test 2 — Dashboard 200 + RemoteIpAddress gotcha:</b> TestServer varsayılan olarak
/// <c>HttpContext.Connection.RemoteIpAddress</c>'i null bırakır → <c>LocalhostDashboardAuthorization</c>
/// fail-closed davranışıyla reddeder. <c>WithWebHostBuilder(...).Configure()</c> minimal hosting'de
/// tüm pipeline'ı replace ettiğinden kullanılamaz (endpoint'ler silinir → 404). Çözüm:
/// <c>IStartupFilter</c> kaydı: filter mevcut pipeline'a <em>prepend</em> edilir, override etmez.
/// Böylece loopback IP middleware dashboard'dan önce çalışır, endpoint'ler korunur.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HangfireSetupTests(ApiTestFactory factory)
{
    [Fact]
    public void AddHangfireServices_InDevelopmentEnvironment_ResolvesIBackgroundJobClient()
    {
        // ApiTestFactory Development ortamında başlatır + PrepareSchemaIfNecessary=true.
        // Services erişimi host'un başladığını garantiler → şema hazır, HangFire storage kurulu.
        var client = factory.Services.GetService<IBackgroundJobClient>();

        client.Should().NotBeNull(
            because: "AddHangfireServices IBackgroundJobClient'ı DI container'a kaydetmelidir");
    }

    [Fact]
    public async Task GetHangfireDashboard_FromLoopbackAddress_Returns200()
    {
        // IStartupFilter: mevcut pipeline'ı replace etmeden, en başa middleware prepend eder.
        // Configure() yaklaşımı minimal hosting'de tüm pipeline'ı siler → endpoint 404.
        // IStartupFilter bu kısıtı aşar: host startup'ında tüm registered filter'lar sırayla
        // uygulanır ve asıl Configure zinciri onlardan sonra çalışır.
        using var loopbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, LoopbackRemoteIpStartupFilter>()));

        var client = loopbackFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var response = await client.GetAsync("/hangfire");

        // Dashboard HTML içeriği döner. Hangfire ilk isteği /hangfire → /hangfire/ olarak
        // redirect edebilir (trailing slash normalizasyonu). AutoRedirect=false olduğundan
        // 302 de kabul edilir; 2xx veya 3xx her ikisi de dashboard'ın erişilebilir olduğunu kanıtlar.
        var statusCode = (int)response.StatusCode;
        statusCode.Should().BeInRange(200, 399,
            because: "Development'ta loopback adresinden /hangfire erişilebilir olmalıdır");
    }

    [Fact]
    public async Task GetHangfireDashboard_FromNullRemoteIp_Returns401()
    {
        // TestServer default: RemoteIpAddress = null → LocalhostDashboardAuthorization.IsLoopback
        // fail-closed → false → Hangfire 401 döner. K3 defense-in-depth doğrulaması.
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "null RemoteIpAddress → fail-closed → dashboard erişimi reddedilmelidir");
    }
}

/// <summary>
/// Test-özel <see cref="IStartupFilter"/>: her request için <c>RemoteIpAddress = Loopback</c>
/// set eden middleware'i pipeline'ın en başına prepend eder. <c>Configure()</c> yaklaşımı
/// minimal hosting'de endpoint pipeline'ını sildiğinden bu pattern tercih edilir.
/// </summary>
file sealed class LoopbackRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await nextMiddleware(context);
            });
            next(app);
        };
}
