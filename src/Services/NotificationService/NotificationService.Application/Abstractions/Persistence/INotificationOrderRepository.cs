using OrderHub.NotificationService.Domain.Orders;

namespace OrderHub.NotificationService.Application.Abstractions.Persistence;

/// <summary>
/// <see cref="OrderProjection"/> okuma ve kalıcılık abstraction'ı.
/// Infrastructure'daki <c>NotificationOrderRepository</c> EF Core üzerinden implement eder (DIP).
/// </summary>
public interface INotificationOrderRepository
{
    Task<OrderProjection?> GetByIdAsync(Guid orderId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
