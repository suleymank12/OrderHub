using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.PaymentService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="PaymentDbContext"/>'i
/// kurabilmesi için factory. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) —
/// <c>migrations add</c> DB'ye bağlanmaz, yalnızca provider'ı belirler; env var ile override edilebilir.
/// </summary>
internal sealed class PaymentDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Payment_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PaymentDbContext(options);
    }
}
