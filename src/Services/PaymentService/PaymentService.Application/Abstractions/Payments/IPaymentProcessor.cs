namespace OrderHub.PaymentService.Application.Abstractions.Payments;

/// <summary>
/// Dış ödeme sağlayıcısı portu (gerçek gateway'in yerine geçer). Faz 3'te deterministik bir mock
/// (<c>MockPaymentProcessor</c>) implement eder; ileride gerçek bir HTTP adapter aynı port'un arkasına geçer.
/// </summary>
public interface IPaymentProcessor
{
    /// <summary>Verilen sipariş/tutar için ödemeyi işler ve sonucu döner.</summary>
    Task<PaymentResult> ProcessAsync(
        Guid orderId, decimal amount, string currency, CancellationToken cancellationToken);
}

/// <summary>
/// Ödeme sağlayıcısının sonucu. Başarıda <see cref="ExternalTransactionId"/> dolu, başarısızlıkta
/// <see cref="FailureReason"/> dolu olur.
/// </summary>
/// <param name="IsSuccess">Ödeme başarılı mı?</param>
/// <param name="ExternalTransactionId">Başarıda sağlayıcının dış işlem kimliği; aksi halde null.</param>
/// <param name="FailureReason">Başarısızlıkta gerekçe; aksi halde null.</param>
public sealed record PaymentResult(bool IsSuccess, string? ExternalTransactionId, string? FailureReason);
