namespace OrderHub.AnalyticsService.Domain.Revenue;

/// <summary>
/// Bir günün gelir aggregate read-model'i (CQRS read-side, ADR-0006). PK = <see cref="Date"/> (gün). OrderPaid
/// event'leriyle güncellenir (ödenen tutar günlük gelire eklenir). ★ Aggregate-güncelleme logic'i (gelir/sayı
/// artışı, ortalama yeniden hesaplama) idempotency ile birlikte 4c-3'te eklenir — burada yalnız alanlar +
/// belirli bir gün için sıfır-satır factory'si (<see cref="Create"/>).
/// </summary>
public sealed class DailyRevenueProjection
{
    private DailyRevenueProjection()
    {
    }

    private DailyRevenueProjection(DateOnly date)
    {
        Date = date;
    }

    /// <summary>Gün (PK; UTC tarih).</summary>
    public DateOnly Date { get; private set; }

    /// <summary>O gün ödenen sipariş sayısı.</summary>
    public int TotalOrders { get; private set; }

    /// <summary>O günün toplam geliri.</summary>
    public decimal TotalRevenue { get; private set; }

    /// <summary>Ortalama sipariş değeri (toplam gelir / sipariş sayısı).</summary>
    public decimal AvgOrderValue { get; private set; }

    /// <summary>Belirli bir gün için sıfırlanmış gelir satırı üretir (ilk OrderPaid'de güncellenir, 4c-3).</summary>
    public static DailyRevenueProjection Create(DateOnly date) => new(date);
}
