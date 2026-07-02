using Microsoft.EntityFrameworkCore;
using OrderHub.NotificationService.Application.Abstractions.Persistence;
using OrderHub.NotificationService.Domain.Orders;

namespace OrderHub.NotificationService.Infrastructure.Persistence;

/// <summary>
/// <see cref="INotificationOrderRepository"/>'nin EF Core implementasyonu (scoped: DbContext ile uyumlu yaşam süresi).
/// <see cref="NotificationDbContext"/> üzerinden <see cref="OrderProjection"/> okur ve değişiklikleri kalıcı kılar.
/// Tracking açık → <c>MarkReminderSent</c> gibi mutation'lar <see cref="SaveChangesAsync"/> ile persist edilir.
/// </summary>
internal sealed class NotificationOrderRepository(NotificationDbContext context) : INotificationOrderRepository
{
    public Task<OrderProjection?> GetByIdAsync(Guid orderId, CancellationToken ct) =>
        context.OrderProjections.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        context.SaveChangesAsync(ct);
}
