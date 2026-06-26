using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.InventoryService.Infrastructure.Persistence;

/// <summary>
/// Pending migration'ları uygulamak için public sözleşme. <see cref="InventoryDbContext"/> internal kaldığından
/// (§9) Api/test'ler onu doğrudan resolve edemez; bu extension *nasıl*'ı (internal DbContext) kapsüller,
/// çağıran *ne zaman*'ı (ADR-0001: yalnızca Development) yönetir. PaymentService precedent'iyle aynı.
/// </summary>
public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
