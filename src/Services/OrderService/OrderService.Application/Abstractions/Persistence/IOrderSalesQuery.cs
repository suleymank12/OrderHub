using OrderHub.OrderService.Application.Orders.Queries;

namespace OrderHub.OrderService.Application.Abstractions.Persistence;

/// <summary>
/// Sipariş satış projeksiyonları için read-query port'u (CQRS read-side). <see cref="IOrderRepository"/>'den
/// ayrıdır: repository <b>aggregate</b> döner, bu port <b>projection DTO</b> döner. Faz 4'te AnalyticsService
/// gerçek read-model'i devralır; bu port o ayrımın OrderService içindeki ilk adımıdır. IQueryable sızdırmaz (§9).
/// </summary>
public interface IOrderSalesQuery
{
    /// <summary>
    /// <paramref name="fromUtc"/> (dahil) – <paramref name="toUtc"/> (hariç) aralığında oluşturulan
    /// siparişleri para birimi bazında özetler. Aralık çağıran tarafından verilir (job "önceki gün"ü
    /// hesaplar) → test edilebilirlik (<c>CreatedAtUtc</c> manipüle edilmez, aralık oynatılır).
    /// </summary>
    Task<IReadOnlyList<DailySalesByCurrency>> GetDailySalesByCurrencyAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);
}
