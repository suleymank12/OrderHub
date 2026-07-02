using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

/// <summary>
/// Bir siparişi ödenmiş işaretler (PaymentSucceeded akışı). Faz 5'te saga, <c>MarkOrderPaidConsumer</c>
/// üzerinden <c>ISender</c> ile tetikler; HTTP ile değil. Sonuç <c>bool</c>: geçiş uygulandıysa <c>true</c>,
/// terminal/idempotent no-op ise <c>false</c>. Sipariş henüz <c>Pending</c> ise (ConfirmOrder bu siparişe daha
/// ulaşmadı — saga Karar D, ConfirmOrder + ProcessPayment paralel) handler <see cref="NotYetConfirmedErrorCode"/>
/// kodlu <b>retryable</b> failure döner → consumer throw → MassTransit retry → ConfirmOrder işlenince başarı.
/// </summary>
/// <param name="OrderId">Ödenmiş işaretlenecek siparişin kimliği.</param>
public sealed record MarkOrderPaidCommand(Guid OrderId) : ICommand<bool>
{
    /// <summary>
    /// Sipariş henüz <c>Pending</c> (ConfirmOrder uygulanmadı) olduğunda dönen retryable failure kodu. Consumer
    /// bu kodu görünce throw eder → MassTransit retry (Faz 5 saga Karar D). Tek kaynak: handler üretir, consumer tüketir.
    /// </summary>
    public const string NotYetConfirmedErrorCode = "Order.NotYetConfirmed";
}
