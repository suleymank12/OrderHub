using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Gateway.Extensions;
using OrderHub.Gateway.IntegrationTests.Fixtures;

namespace OrderHub.Gateway.IntegrationTests;

/// <summary>
/// Faz 6 6a-2 — gateway merkezi sağlık panosu (HealthChecks.UI, §6.4). Bu testler dashboard'un <b>ayakta</b> ve
/// config'in (6 servis /health/ready poll'ü) <b>yüklü</b> olduğunu deterministik doğrular.
/// <para>
/// ★ DÜRÜST SINIR: Gerçek 6-servis poll'ü (yeşil/kırmızı geçişleri) 6 canlı servis gerektirir → burada
/// doğrulanamaz; downstream'siz WebApplicationFactory'de poller "orderservice" gibi compose adlarını DNS ile
/// çözemez (beklenen). Gerçek uçtan-uca poll, tam-stack <b>fresh-volume smoke</b>'ında (Faz 6 sonu) doğrulanır.
/// Buradaki testler: (a) config'in 6 endpoint'i kaydettiğini (poll timing'inden BAĞIMSIZ, DI'dan Settings okuyarak),
/// (b) UI + API endpoint'lerinin erişilebilir olduğunu kanıtlar. Sahte "6 servis poll oldu" iddiası YOK.
/// </para>
/// </summary>
[Collection(GatewayAppCollection.Name)]
public sealed class HealthDashboardTests(GatewayAppFixture app)
{
    private static readonly string[] ExpectedServices =
    [
        "orderservice", "paymentservice", "analyticsservice",
        "inventoryservice", "orderprocessingservice", "notificationservice",
    ];

    [Fact]
    public void HealthChecksUi_Configuration_RegistersSixDownstreamReadyEndpoints()
    {
        // ★ Poll timing'inden bağımsız, deterministik: HealthChecks.UI'nin config-driven okuduğu "HealthChecksUI"
        // bölümünü doğrudan bağlı IConfiguration'dan doğrula (AddGatewayHealthChecksUi bunu AddHealthChecksUI ile okur).
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        var endpoints = configuration.GetSection("HealthChecksUI:HealthChecks").GetChildren().ToList();

        endpoints.Should().HaveCount(6, "6 downstream servisin /health/ready'si config-driven kaydedilmeli");
        endpoints.Select(e => e["Name"]).Should().BeEquivalentTo(ExpectedServices);
        endpoints.Should().OnlyContain(
            e => e["Uri"]!.EndsWith("/health/ready", StringComparison.Ordinal),
            "her endpoint downstream readiness probe'unu poll etmeli");
    }

    [Fact]
    public async Task HealthUiApi_IsReachable_ReturnsSuccess_DashboardHostUp()
    {
        var client = app.CreateClient();

        // ★ Dashboard'un poll-sonuç API'si ayakta (downstream olmasa da host çalışır; poll geçmişi başta boş).
        var response = await client.GetAsync(HealthChecksUiExtensions.ApiPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "dashboard API endpoint'i erişilebilir olmalı");
    }

    [Fact]
    public async Task HealthUi_Page_IsReachable_ReturnsUiShell()
    {
        var client = app.CreateClient();

        // ★ Tarayıcı UI'si tek edge (gateway) üzerinden servis ediliyor (SPA shell 200).
        var response = await client.GetAsync(HealthChecksUiExtensions.UiPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "dashboard UI sayfası erişilebilir olmalı");
    }
}
