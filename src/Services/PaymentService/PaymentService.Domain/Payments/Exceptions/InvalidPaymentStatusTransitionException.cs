using OrderHub.Common.Exceptions;

namespace OrderHub.PaymentService.Domain.Payments.Exceptions;

/// <summary>
/// Geçersiz bir ödeme durum geçişi denendiğinde fırlatılır (ör. zaten Succeeded/Failed olan bir ödemeyi
/// yeniden sonuçlandırmak). Terminal durumlar yeniden işlenemez → idempotency korunur.
/// </summary>
public sealed class InvalidPaymentStatusTransitionException : DomainException
{
    /// <summary>Mevcut (kaynak) durum.</summary>
    public PaymentStatus From { get; }

    /// <summary>Hedeflenen durum.</summary>
    public PaymentStatus To { get; }

    /// <summary>Kaynak ve hedef durumla exception oluşturur.</summary>
    public InvalidPaymentStatusTransitionException(PaymentStatus from, PaymentStatus to)
        : base($"Cannot transition payment from {from} to {to}.")
    {
        From = from;
        To = to;
    }
}
