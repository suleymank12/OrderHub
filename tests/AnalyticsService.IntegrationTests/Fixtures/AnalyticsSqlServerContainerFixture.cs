using Microsoft.EntityFrameworkCore;
using OrderHub.AnalyticsService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, <see cref="AnalyticsDbContext"/>
/// migration'larını uygulayan fixture (PaymentService pattern'i). Image pinli. Connection string seam'i ile
/// testler gerçek <c>OrderHub_Analytics</c> şemasına bağlanır.
/// </summary>
public sealed class AnalyticsSqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    /// <summary>Migrasyonları uygulanmış container'ın connection string'i.</summary>
    internal string ConnectionString => _container.GetConnectionString();

    /// <summary>Container'a bağlı yeni bir <see cref="AnalyticsDbContext"/> üretir.</summary>
    internal AnalyticsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
