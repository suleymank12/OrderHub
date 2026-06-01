using Microsoft.EntityFrameworkCore;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IOrderRepository"/>'nin EF Core implementasyonu. Yalnızca persistence; iş kuralı içermez.
/// <c>IQueryable</c> dışarı sızdırmaz — tüm sorgular burada materialize edilir.
/// </summary>
internal sealed class OrderRepository(OrderDbContext context) : IOrderRepository
{
    // Tracking'e ekler; kalıcılaştırma IUnitOfWork.SaveChangesAsync ile (TransactionBehavior).
    public void Add(Order order) => context.Orders.Add(order);

    // Tracking AÇIK: GetById command handler'larında load-modify-save için de kullanılır.
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Read-only liste → AsNoTracking (change tracking yükü yok).
        var baseQuery = context.Orders.AsNoTracking();

        // Count, Include'dan bağımsız → doğru toplam.
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(order => order.CreatedAtUtc) // en yeni sipariş önce.
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(order => order.Items)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
