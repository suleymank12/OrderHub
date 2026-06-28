namespace OrderHub.OrderService.Infrastructure.Messaging;

/// <summary>
/// <see cref="MarkOrderPaidConsumer"/>'ın sipariş henüz <c>Pending</c> iken (ConfirmOrder bu siparişe daha
/// ulaşmadı — saga Karar D) bilinçli olarak fırlattığı <b>retry sinyali</b>. Bir bug değildir: MassTransit
/// bunu yakalayıp mesajı retry eder; bu arada ConfirmOrder işlenir → sonraki denemede sipariş Confirmed →
/// MarkPaid başarılı. Açık tip → log/retry niyeti kendini belgeler (çıplak <c>InvalidOperationException</c> değil).
/// </summary>
internal sealed class OrderNotYetConfirmedException(Guid orderId)
    : Exception($"Order '{orderId}' is not yet confirmed; retrying mark-paid until ConfirmOrder is applied.")
{
    /// <summary>Henüz onaylanmamış (Pending) siparişin kimliği.</summary>
    public Guid OrderId { get; } = orderId;
}
