using Microsoft.EntityFrameworkCore;
using OrderHub.InventoryService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.InventoryService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, <see cref="InventoryDbContext"/>
/// migration'larını uygulayan fixture (Payment/Analytics pattern'i). Image pinli. Connection string seam'i ile
/// testler gerçek <c>OrderHub_Inventory</c> şemasına bağlanır → EF eşlemesi (backing-field collection, shadow FK,
/// RowVersion) gerçek DB'de doğrulanır.
/// </summary>
public sealed class InventorySqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    /// <summary>Container'a bağlı yeni bir <see cref="InventoryDbContext"/> üretir (internal — InternalsVisibleTo).</summary>
    internal InventoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
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
