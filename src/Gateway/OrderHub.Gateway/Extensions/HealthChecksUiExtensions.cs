namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway merkezi sağlık panosu (HealthChecks.UI, §6.4). 6 downstream servisin <c>/health/ready</c> endpoint'ini
/// periyodik poll eder ve tek bir dashboard'da yeşil/kırmızı gösterir. <b>Config-driven</b>: poll edilecek
/// endpoint'ler ve aralık <c>HealthChecksUI</c> appsettings bölümünden okunur (compose servis adları). Depo,
/// <b>InMemory storage</b> kullanır — gateway saf-edge/stateless olduğundan poll geçmişi RAM'de tutulur (kalıcı DB
/// YOK; K3/izolasyon: gateway'in DB/migration'ı olmaz). ★ Saf edge korunur: servislere yalnız HTTP poll atılır,
/// hiçbir ProjectReference yok.
/// </summary>
internal static class HealthChecksUiExtensions
{
    /// <summary>Dashboard UI (tarayıcı) yolu — tek edge (gateway :8000) üzerinden erişilir.</summary>
    public const string UiPath = "/health-ui";

    /// <summary>Dashboard'un poll sonuçlarını sunduğu JSON API yolu (UI bunu tüketir; test bununla doğrular).</summary>
    public const string ApiPath = "/health-ui-api";

    public static IServiceCollection AddGatewayHealthChecksUi(this IServiceCollection services)
    {
        // AddHealthChecksUI: "HealthChecksUI" config bölümünü otomatik okur (endpoint listesi + EvaluationTimeInSeconds).
        // AddInMemoryStorage: poll sonuçları RAM'de (gateway stateless → SQL storage kurmayız, K3/saf-edge).
        services.AddHealthChecksUI().AddInMemoryStorage();
        return services;
    }

    public static WebApplication MapGatewayHealthChecksUi(this WebApplication app)
    {
        // UI + API sabit yollarda (default /healthchecks-ui yerine tutarlı /health-ui, gateway'in /health/live'ıyla uyumlu ad).
        app.MapHealthChecksUI(options =>
        {
            options.UIPath = UiPath;
            options.ApiPath = ApiPath;
        });
        return app;
    }
}
