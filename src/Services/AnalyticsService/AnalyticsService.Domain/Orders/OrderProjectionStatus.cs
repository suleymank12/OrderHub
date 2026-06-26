namespace OrderHub.AnalyticsService.Domain.Orders;

/// <summary>
/// Sipariş projection'ının yaşam-döngüsü durumu — Kafka order-stream event'lerinden türetilir
/// (OrderCreated → Created, OrderConfirmed → Confirmed, OrderPaid → Paid, OrderCancelled → Cancelled).
/// OrderService domain <c>OrderStatus</c>'uyla tutarlı; kolonda <b>string</b> olarak saklanır (HasConversion).
/// </summary>
public enum OrderProjectionStatus
{
    Created = 0,
    Confirmed = 1,
    Paid = 2,
    Cancelled = 3,
}
