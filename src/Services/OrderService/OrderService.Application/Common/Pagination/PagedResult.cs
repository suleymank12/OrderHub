namespace OrderHub.OrderService.Application.Common.Pagination;

/// <summary>
/// Sayfalı bir sorgu sonucunu ve sayfalama metadata'sını taşıyan generic, immutable primitive.
/// Tek bir aggregate'e bağlı değildir; ileride başka listeler de bunu kullanır (bu yüzden
/// <c>Common</c> altında).
/// </summary>
/// <typeparam name="T">Sayfadaki öğelerin tipi.</typeparam>
/// <param name="Items">Geçerli sayfadaki öğeler.</param>
/// <param name="Page">1-tabanlı geçerli sayfa numarası.</param>
/// <param name="PageSize">Sayfa başına öğe sayısı.</param>
/// <param name="TotalCount">Tüm sayfalardaki toplam öğe sayısı.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Toplam sayfa sayısı.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Sonraki sayfa var mı?</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Önceki sayfa var mı?</summary>
    public bool HasPreviousPage => Page > 1;
}
