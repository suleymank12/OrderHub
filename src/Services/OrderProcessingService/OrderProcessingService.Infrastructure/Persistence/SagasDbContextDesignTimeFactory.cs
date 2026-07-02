using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="SagasDbContext"/>'i kurabilmesi
/// için factory. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) — <c>migrations add</c>
/// DB'ye bağlanmaz, yalnızca provider'ı belirler; env var ile override edilebilir. InventoryService precedent'i.
/// </summary>
internal sealed class SagasDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SagasDbContext>
{
    public SagasDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Sagas_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<SagasDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SagasDbContext(options);
    }
}
