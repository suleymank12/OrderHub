using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OrderHub.AnalyticsService.Infrastructure.Messaging;
using OrderHub.AnalyticsService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>
/// Read-only Api'yi gerçek SQL Server container'ına karşı in-process host'layan test factory (OrderService
/// <c>ApiTestFactory</c> pattern'i). EF InMemory'e düşmeyiz (converter/şema gerçek sağlayıcıda doğrulanır).
/// <para>
/// ★ Kafka consumer (<see cref="OrderEventsConsumer"/>) HostedService'i kaldırılır: read-API consumer'dan
/// bağımsızdır; bırakılsaydı host start'ta Kafka broker'a bağlanmaya çalışırdı (testte broker yok). Consumer
/// round-trip'i ayrıca <c>OrderEventsConsumerEndToEndTests</c> (gerçek Kafka container) kapsar.
/// </para>
/// <para>
/// Connection string ve JWT ayarları <b>env var ile</b> verilir (in-memory config DEĞİL): minimal hosting'de
/// <c>Program</c> <c>AddInfrastructure(builder.Configuration)</c>'ı servis-kayıt anında (eager) çağırır;
/// <see cref="WebApplicationFactory{TEntryPoint}"/>'nin config override'ı daha geç (<c>builder.Build()</c>)
/// çalışır. Env var'lar <c>CreateBuilder</c>'da en başta okunduğundan tek güvenilir kanaldır. Assembly genelinde
/// test paralelizasyonu kapalı (AssemblyInfo) → env var izolasyonu korunur. <see cref="DisposeAsync"/> temizler.
/// </para>
/// </summary>
public sealed class AnalyticsApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Deterministik test ayarları (secret ≥32 char = 256-bit HMAC-SHA256). Issuer/Audience üretilen token ile eşleşir.
    private const string TestJwtSecret = "analytics-integration-test-secret-0123456789";
    private const string TestJwtIssuer = "analytics-tests";
    private const string TestJwtAudience = "analytics-tests-audience";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU13-ubuntu-22.04")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // ÖNEMLİ: env var'lar Services erişiminden (host build) ÖNCE set edilmeli ki CreateBuilder okusun.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("JwtSettings__Secret", TestJwtSecret);
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("JwtSettings__Audience", TestJwtAudience);

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>Test'ler arası temiz başlangıç: projection + revenue tabloları boşaltılır.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM OrderProjections");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM DailyRevenueProjections");
    }

    /// <summary>DB'ye doğrudan (HTTP'siz) bir <see cref="AnalyticsDbContext"/> scope'u verir → deterministik seed.</summary>
    internal async Task SeedAsync(Func<AnalyticsDbContext, Task> seed)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        await seed(context);
        await context.SaveChangesAsync();
    }

    /// <summary>Geçerli (test secret/issuer/audience ile imzalı) bir bearer token taşıyan client üretir.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        return client;
    }

    private static string CreateToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development); // Swagger/CORS + startup migration path (sibling pattern).

        // ★ Read-API testleri consumer istemez → Kafka'ya bağlanan HostedService'i kaldır (broker yok). IConsumer
        // singleton'ı yalnız bu HostedService tarafından resolve edildiğinden artık hiç build edilmez.
        builder.ConfigureTestServices(services =>
        {
            var consumerDescriptors = services
                .Where(descriptor => descriptor.ImplementationType == typeof(OrderEventsConsumer))
                .ToList();

            foreach (var descriptor in consumerDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    }

    public new async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("JwtSettings__Secret", null);
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", null);
        Environment.SetEnvironmentVariable("JwtSettings__Audience", null);
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
