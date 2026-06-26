using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.AnalyticsService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="AnalyticsDbContext"/>'i
/// kurabilmesi için factory. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) —
/// <c>migrations add</c> DB'ye bağlanmaz, yalnız provider'ı belirler; env var ile override edilebilir.
/// </summary>
internal sealed class AnalyticsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Analytics_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AnalyticsDbContext(options);
    }
}
