namespace OrderHub.OrderService.Application.Orders.Queries;

/// <summary>
/// Bir gün içinde belirli bir para biriminde oluşturulan siparişlerin özeti. Para birimleri arası toplama
/// anlamsız olduğundan (TRY + USD = tek sayı DEĞİL) gelir <b>currency bazında</b> raporlanır.
/// </summary>
/// <param name="Currency">ISO 4217 para birimi kodu (örn. "TRY").</param>
/// <param name="OrderCount">O para biriminde o gün oluşturulan sipariş sayısı.</param>
/// <param name="Revenue">O para birimindeki toplam sipariş tutarı (brüt booked değer).</param>
public sealed record DailySalesByCurrency(string Currency, int OrderCount, decimal Revenue);
