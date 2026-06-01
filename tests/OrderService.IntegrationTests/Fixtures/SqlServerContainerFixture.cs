using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek SQL Server container'ı (Testcontainers) ayağa kaldıran, migration'ları uygulayan ve test'lere
/// <see cref="OrderDbContext"/> üreten fixture. EF InMemory provider'a düşmeyiz: owned type / value
/// converter / cascade davranışları yalnızca gerçek sağlayıcıda doğrulanır (InMemory false-positive üretir).
/// Image pinli (reproducibility). Collection fixture ile session başına 1 container (hız).
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Gerçek migration'ı uygula (sadece model değil, üretilen şemayı doğrular).
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Container'a bağlı yeni bir <see cref="OrderDbContext"/> üretir.
    /// <paramref name="publisher"/> verilmezse <see cref="NullPublisher"/> kullanılır: dispatch
    /// davranışını doğrulamayan testler event yayınını umursamaz; doğrulayan testler Moq inject eder.
    /// </summary>
    internal OrderDbContext CreateContext(IPublisher? publisher = null)
    {
        var effectivePublisher = publisher ?? NullPublisher.Instance;

        // Interceptor'ı dahil et → test context'i production (AddInfrastructure) ile aynı davranır.
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .AddInterceptors(new DispatchDomainEventsInterceptor(effectivePublisher))
            .Options;

        return new OrderDbContext(options);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
