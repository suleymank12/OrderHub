using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Abstractions.Persistence;

/// <summary>
/// <see cref="Order"/> aggregate'i için persistence portu. Application'da tanımlıdır, Infrastructure
/// (Faz 1.4) EF Core ile implement eder (DIP). Bir <b>port</b>'tur: yalnızca persistence; iş kuralı
/// içermez ve asla <c>IQueryable</c> sızdırmaz (§9) — tüm sorgular burada materialize edilir.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Aggregate'i takibe ekler. Sync'tir çünkü Id client-side üretilir (<see cref="Order.Create"/>),
    /// EF'in <c>AddAsync</c>'ine yalnızca server-side value generator (HiLo) senaryosunda ihtiyaç olur.
    /// Kalıcılaştırma <see cref="IUnitOfWork.SaveChangesAsync"/> ile (TransactionBehavior tarafından) yapılır.
    /// </summary>
    void Add(Order order);

    /// <summary>Id ile siparişi (kalemleriyle) yükler; bulunamazsa <c>null</c> döner.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sayfalı sipariş listesi döner. Sonuç repository içinde materialize edilir; toplam kayıt sayısı
    /// (sayfalama metadata'sı için) ile birlikte döner.
    /// </summary>
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
