namespace OrderHub.OrderService.Application.Orders.Configuration;

/// <summary>
/// Ödenmeyen sipariş zaman aşımı politikası. <see cref="UnpaidTimeout"/> sonunda Pending sipariş otomatik
/// iptal edilir (ana yol: gecikmeli job); <see cref="SweepCron"/> ile çalışan reconciliation job kaçanları
/// yakalar (ADR-0003 hibrit). Config'ten okunur (binding Infrastructure'da); integration testte kısaltılır.
/// </summary>
public sealed class OrderTimeoutOptions
{
    /// <summary>appsettings bölüm adı.</summary>
    public const string SectionName = "OrderTimeout";

    /// <summary>Bir Pending siparişin ödenmeden kalabileceği süre; sonunda otomatik iptal (varsayılan 15 dk).</summary>
    public TimeSpan UnpaidTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Sweep backstop job'unun CRON ifadesi (varsayılan her 5 dk). UTC yorumlanır (ADR-0003 Karar 4).</summary>
    public string SweepCron { get; init; } = "*/5 * * * *";
}
