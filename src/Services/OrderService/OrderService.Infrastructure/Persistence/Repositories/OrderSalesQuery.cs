using Microsoft.EntityFrameworkCore;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Queries;

namespace OrderHub.OrderService.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IOrderSalesQuery"/>'nin EF Core implementasyonu. Salt-okunur projeksiyon (AsNoTracking).
/// Para birimi bazında gruplar; <c>GROUP BY</c> + <c>COUNT</c>/<c>SUM</c> DB tarafında çalışır. Owned
/// <c>Total.Currency</c> (enum→string kolon) grup anahtarı materialize edilip <c>.ToString()</c> bellekte
/// ISO koduna çevrilir (translation riski taşımadan). IQueryable dışarı sızdırmaz (§9).
/// </summary>
internal sealed class OrderSalesQuery(OrderDbContext context) : IOrderSalesQuery
{
    public async Task<IReadOnlyList<DailySalesByCurrency>> GetDailySalesByCurrencyAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        // [fromUtc, toUtc): yarı-açık aralık → gün sınırında çift sayım olmaz.
        var grouped = await context.Orders
            .AsNoTracking()
            .Where(order => order.CreatedAtUtc >= fromUtc && order.CreatedAtUtc < toUtc)
            .GroupBy(order => order.Total.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                OrderCount = group.Count(),
                Revenue = group.Sum(order => order.Total.Amount)
            })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(row => new DailySalesByCurrency(row.Currency.ToString(), row.OrderCount, row.Revenue))
            .ToList();
    }
}
