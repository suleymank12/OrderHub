using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.InventoryService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="InventoryDbContext"/>'i
/// kurabilmesi için factory. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) —
/// <c>migrations add</c> DB'ye bağlanmaz, yalnızca provider'ı belirler; env var ile override edilebilir.
/// </summary>
internal sealed class InventoryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Inventory_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new InventoryDbContext(options);
    }
}
