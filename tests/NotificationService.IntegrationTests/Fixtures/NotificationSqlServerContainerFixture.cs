using Microsoft.EntityFrameworkCore;
using OrderHub.NotificationService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, <see cref="NotificationDbContext"/>
/// migration'larını uygulayan fixture. Image pinli. Connection string seam'i ile testler gerçek
/// <c>OrderHub_Notifications</c> şemasına bağlanır. AnalyticsService fixture pattern'inin aynısı.
/// </summary>
public sealed class NotificationSqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    /// <summary>Migration'ları uygulanmış container'ın connection string'i.</summary>
    internal string ConnectionString => _container.GetConnectionString();

    /// <summary>Container'a bağlı yeni bir <see cref="NotificationDbContext"/> üretir.</summary>
    internal NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
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
