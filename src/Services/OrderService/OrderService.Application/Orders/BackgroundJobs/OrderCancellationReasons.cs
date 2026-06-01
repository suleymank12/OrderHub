namespace OrderHub.OrderService.Application.Orders.BackgroundJobs;

/// <summary>
/// Sistem-üretimli iptal gerekçeleri (sabit). Domain serbest metin kabul eder; bu sabitler otomatik
/// akışların tutarlı, audit-edilebilir gerekçe üretmesini sağlar (magic string yok).
/// </summary>
internal static class OrderCancellationReasons
{
    /// <summary>Sipariş, ödeme süresi içinde ödenmediği için otomatik iptal edildi.</summary>
    public const string PaymentTimeout = "payment_timeout";
}
