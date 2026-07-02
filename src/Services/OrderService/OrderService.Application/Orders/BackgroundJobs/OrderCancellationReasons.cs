namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Sistem-üretimli iptal gerekçeleri (sabit). Domain serbest metin kabul eder; bu sabitler otomatik
/// akışların tutarlı, audit-edilebilir gerekçe üretmesini sağlar (magic string yok).
/// </summary>
internal static class OrderCancellationReasons
{
    /// <summary>Sipariş, ödeme süresi içinde ödenmediği için otomatik iptal edildi.</summary>
    public const string PaymentTimeout = "payment_timeout";

    /// <summary>Sipariş, ödeme sağlayıcısı ödemeyi reddettiği için iptal edildi (PaymentFailed akışı).</summary>
    public const string PaymentFailed = "payment_failed";

    /// <summary>Sipariş, stok ayrılamadığı için iptal edildi (saga StockReservationFailed telafisi, Faz 5e).</summary>
    public const string StockUnavailable = "stock_unavailable";
}
