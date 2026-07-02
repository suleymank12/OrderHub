using Hangfire;
using Hangfire.SqlServer;

namespace OrderHub.InventoryService.Api.BackgroundJobs;

/// <summary>
/// Hangfire kayıt yardımcısı (ADR-0007 Karar 2): 15dk reservation expiry job'unu (recurring, Infrastructure'daki
/// <c>RecurringJobRegistrar</c> kaydeder) çalıştıracak server'ı bu API process'inde başlatır. Storage = domain
/// DB'si (<c>OrderHub_Inventory</c>) içinde ayrı <c>HangFire</c> şeması (ADR-0003, OrderService precedent'i).
/// Schema-prep yalnızca Development (ADR-0001 prod-safe mirror). <b>Dashboard YOK</b> (YAGNI — JWT yok, UI gereksiz).
/// </summary>
internal static class HangfireExtensions
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string HangfireSchemaName = "HangFire";

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
                // ★ DB, EF migration'ı ile bu schema-prep'ten ÖNCE oluşmalı (Program.cs build()-sonrası migrate;
                // Faz 3 cold-start dersi: yoksa 'HangFire.Hash' eksik → recurring registrar crash).
                PrepareSchemaIfNecessary = environment.IsDevelopment(),
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));

        services.AddHangfireServer();

        // Retry policy: Hangfire varsayılan 10-deneme yerine 3 (OrderService precedent'i). Global filtre → tüm job'lar.
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
}
