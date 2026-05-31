using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.OrderService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="OrderDbContext"/>'i
/// kurabilmesi için factory. Api host'u henüz DI-wired olmadığından (Faz 1.5) EF context'i app'ten
/// türetemez. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) — <c>migrations add</c>
/// DB'ye bağlanmaz, yalnızca provider'ı belirler; gerçek değer env var ile override edilebilir.
/// </summary>
internal sealed class OrderDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Order_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new OrderDbContext(options);
    }
}
