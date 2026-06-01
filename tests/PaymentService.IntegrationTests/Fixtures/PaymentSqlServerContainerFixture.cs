using Microsoft.EntityFrameworkCore;
using OrderHub.PaymentService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.PaymentService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, <see cref="PaymentDbContext"/>
/// migration'larını uygulayan fixture — OrderService <c>SqlServerContainerFixture</c> pattern'i. Image pinli.
/// Connection string seam'i ile testler gerçek <c>AddInfrastructure</c> DI'ını bu DB'ye kurar.
/// </summary>
public sealed class PaymentSqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    /// <summary>Migrasyonları uygulanmış container'ın connection string'i.</summary>
    internal string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Gerçek migration'ı uygula (sadece model değil, üretilen şemayı doğrular). Interceptor gerekmez.
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;
        await using var context = new PaymentDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
