using OrderHub.PaymentService.Application.Abstractions.Messaging;

namespace OrderHub.PaymentService.Application.Payments.Commands.ProcessPayment;

/// <summary>
/// Bir sipariş için ödemeyi işleyen command. Başarıda oluşturulan ödemenin Id'sini döner. Faz 3c'de bu
/// command RabbitMQ consumer'ından (ProcessPaymentIntegrationEvent) tetiklenecek; HTTP ile çağrılmaz.
/// Alanlar primitive (Money yok) → PaymentService OrderService domain'ine bağlanmaz.
/// </summary>
/// <param name="OrderId">Ödemesi işlenecek sipariş kimliği.</param>
/// <param name="Amount">Çekilecek tutar.</param>
/// <param name="Currency">Para birimi (ISO 4217 kodu).</param>
public sealed record ProcessPaymentCommand(Guid OrderId, decimal Amount, string Currency) : ICommand<Guid>;
