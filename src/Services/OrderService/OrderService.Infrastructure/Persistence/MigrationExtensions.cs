using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.OrderService.Infrastructure.Persistence;

/// <summary>
/// Pending migration'ları uygulamak için public sözleşme. <see cref="OrderDbContext"/> internal kaldığından
/// (§9 encapsulation) Api/test'ler onu doğrudan resolve edemez; bu extension *nasıl*'ı (internal DbContext)
/// kapsüller, çağıran *ne zaman*'ı (ADR-0001: yalnızca Development) yönetir. <see cref="IServiceProvider"/>
/// üzerinde tanımlı → host, integration test ve gelecek worker servisler aynı sözleşmeyi kullanır.
/// </summary>
public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
