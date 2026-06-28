using Microsoft.EntityFrameworkCore;
using OrderHub.OrderProcessingService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, <see cref="SagasDbContext"/> migration'larını
/// uygulayan fixture (InventoryService pattern'i). Image pinli. Connection string seam'i ile testler gerçek
/// <c>OrderHub_Sagas</c> şemasına bağlanır → saga state EF eşlemesi (PK, RowVersion, JSON küme kolonları)
/// gerçek DB'de doğrulanır.
/// </summary>
public sealed class SagasSqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    /// <summary>Migrasyonları uygulanmış container'ın connection string'i.</summary>
    internal string ConnectionString => _container.GetConnectionString();

    /// <summary>Container'a bağlı yeni bir <see cref="SagasDbContext"/> üretir (internal — InternalsVisibleTo).</summary>
    internal SagasDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SagasDbContext>()
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
