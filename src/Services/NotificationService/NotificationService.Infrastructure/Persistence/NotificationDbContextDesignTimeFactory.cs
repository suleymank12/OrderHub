using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderHub.NotificationService.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI'ın (<c>dotnet ef migrations add</c>) design-time'da <see cref="NotificationDbContext"/>'i
/// kurabilmesi için factory. Buradaki connection string <b>placeholder</b>'dır (secret değil, K3) —
/// <c>migrations add</c> DB'ye bağlanmaz, yalnız provider'ı belirler; env var ile override edilebilir.
/// </summary>
internal sealed class NotificationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=OrderHub_Notifications_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new NotificationDbContext(options);
    }
}
