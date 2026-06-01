using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderHub.OrderService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Api'yi gerçek SQL Server container'ına karşı in-process host'layan test factory. EF InMemory'e
/// düşmeyiz (owned type/converter/cascade gerçek sağlayıcıda doğrulanır).
/// <para>
/// Connection string ve JWT secret <b>env var ile</b> verilir, in-memory config ile DEĞİL: minimal
/// hosting'de <c>Program</c> <c>AddInfrastructure(builder.Configuration)</c>'ı servis kayıt anında
/// (eager) çağırır; <see cref="WebApplicationFactory{TEntryPoint}"/>'nin <c>ConfigureAppConfiguration</c>'ı
/// <c>builder.Build()</c>'de — DAHA GEÇ — çalışır, bu yüzden in-memory override görülmez. Env var'lar
/// <c>CreateBuilder</c>'da en başta okunduğundan tek güvenilir kanaldır. İzolasyon korunur: tüm Api
/// testleri aynı değerleri paylaşır, Infra testleri bu env var'ları okumaz; <see cref="DisposeAsync"/>
/// temizler. Ek güvence: assembly genelinde test paralelizasyonu kapalı (CollectionBehavior).
/// </para>
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Deterministik test secret (≥32 char = 256-bit HMAC-SHA256). Random değil → debugging kolay.
    private const string TestJwtSecret = "integration-test-jwt-secret-key-0123456789";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // ÖNEMLİ: env var'lar Services erişiminden (host build) ÖNCE set edilmeli ki CreateBuilder okusun.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("JwtSettings__Secret", TestJwtSecret);

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>Test'ler arası temiz başlangıç: Orders silinir (cascade ile OrderItems da). HTTP integration'da
    /// app kendi DbContext'ini kullandığından transaction-rollback uygulanamaz → reset bu yolla yapılır.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Orders");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development); // dev token endpoint + Swagger map'lensin.

        // Production RabbitMQ MassTransit kaydını tamamen kaldırıp in-memory test harness ile değiştir →
        // testte broker'a HİÇ bağlanılmaz (connection-retry gürültüsü olmaz). OutboxProcessorService
        // (HostedService) bu in-memory bus üzerinden publish edebilir; mevcut testlerde Confirm HTTP ucu
        // olmadığından outbox satırı yazılmaz → processor boş polling yapar (no-op, BatchFailed loop'u olmaz).
        builder.ConfigureTestServices(services =>
        {
            RemoveMassTransitRegistrations(services);
            services.AddMassTransitTestHarness();
        });
    }

    // MassTransit'in tüm DI kayıtlarını (RabbitMQ transport + bus hosted service dahil) kaldırır →
    // AddMassTransitTestHarness temiz zeminde in-memory transport kurar ("already registered" çakışması olmaz).
    // IIntegrationEventPublisher (OrderHub.EventBus namespace) kalır → harness'in in-memory IPublishEndpoint'ini kullanır.
    private static void RemoveMassTransitRegistrations(IServiceCollection services)
    {
        var massTransitDescriptors = services
            .Where(descriptor =>
                IsMassTransitType(descriptor.ServiceType) ||
                IsMassTransitType(descriptor.ImplementationType) ||
                IsMassTransitType(descriptor.ImplementationInstance?.GetType()))
            .ToList();

        foreach (var descriptor in massTransitDescriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsMassTransitType(Type? type) =>
        type?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;

    public new async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("JwtSettings__Secret", null);
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
