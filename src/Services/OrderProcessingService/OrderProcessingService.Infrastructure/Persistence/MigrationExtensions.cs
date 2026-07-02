using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence;

/// <summary>
/// Pending migration'ları uygulamak için public sözleşme. <see cref="SagasDbContext"/> internal kaldığından
/// (§9) Api/test'ler onu doğrudan resolve edemez; bu extension <i>nasıl</i>'ı (internal DbContext) kapsüller,
/// çağıran <i>ne zaman</i>'ı (ADR-0001: yalnızca Development) yönetir. InventoryService precedent'iyle aynı.
/// </summary>
public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SagasDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
