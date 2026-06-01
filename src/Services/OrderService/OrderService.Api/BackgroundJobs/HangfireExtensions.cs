using Hangfire;
using Hangfire.SqlServer;

namespace OrderHub.OrderService.Api.BackgroundJobs;

/// <summary>
/// Hangfire kayıt ve dashboard map yardımcıları. Storage olarak domain DB'si (<c>OrderHub_Order</c>)
/// içinde ayrı bir <c>HangFire</c> şeması kullanılır (ADR-0003) — ayrı bir Hangfire DB'si kurulmaz.
/// İş argümanlarını Hangfire internal olarak Newtonsoft.Json ile serialize eder (bilinçli §3 istisnası,
/// ADR-0003 §6); bu yüzden job arg'ları primitive (<c>Guid</c>) tutulur.
/// </summary>
internal static class HangfireExtensions
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string HangfireSchemaName = "HangFire";
    private const string DashboardPath = "/hangfire";

    /// <summary>
    /// Hangfire'ı SQL Server storage ile kaydeder ve job'ları işleyecek server'ı bu API process'inde başlatır
    /// (ADR-0003: yük artarsa ayrı worker'a taşınır). Schema hazırlama yalnızca Development'ta yapılır —
    /// Production'da otomatik schema YOK (ADR-0001 prod-safe pattern'inin Hangfire'a uygulanışı).
    /// </summary>
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        // Fail-fast: eksik connection string → net hata (cryptic Hangfire runtime hatası yerine), K3/Infra ile tutarlı.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                // ADR-0003: domain DB'si içinde izole 'HangFire' şeması (default 'dbo'yu kirletmez).
                SchemaName = HangfireSchemaName,
                // ADR-0003 + ADR-0001 mirror: prod'da otomatik schema YOK; yalnızca Development'ta hazırlanır.
                PrepareSchemaIfNecessary = environment.IsDevelopment(),
                // Hangfire'ın önerilen modern SQL storage ayarları → polling/global-lock yükünü azaltır.
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));

        services.AddHangfireServer();

        // Retry policy (ROADMAP §2.2): Hangfire'ın varsayılan 10-deneme AutomaticRetry'ını 3 ile değiştir.
        // Global filtre → tüm job'lara uygulanır, Application job'larına Hangfire attribute eklemeden (C1).
        // Backoff varsayılan (üstel) bırakılır. Remove+Add deterministik ve idempotenttir (çift kayıt olmaz).
        var existingRetryFilters = GlobalJobFilters.Filters
            .Where(filter => filter.Instance is AutomaticRetryAttribute)
            .Select(filter => filter.Instance)
            .ToList();
        foreach (var filterInstance in existingRetryFilters)
        {
            GlobalJobFilters.Filters.Remove(filterInstance);
        }

        GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3 });

        return services;
    }

    /// <summary>
    /// Hangfire dashboard'ını <c>/hangfire</c>'da map'ler — <b>yalnızca Development'ta</b>. Production'da
    /// endpoint kod yolundan hiç eklenmez (attack surface sıfır, dev-token endpoint pattern'iyle simetrik).
    /// Dev'de erişim ayrıca <see cref="LocalhostDashboardAuthorization"/> ile localhost'a kısıtlanır.
    /// </summary>
    public static WebApplication MapHangfireDashboardDevOnly(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHangfireDashboard(DashboardPath, new DashboardOptions
            {
                Authorization = [new LocalhostDashboardAuthorization()],
                DisplayStorageConnectionString = false, // K3: connection string UI'da görünmez.
            });
        }

        return app;
    }
}
