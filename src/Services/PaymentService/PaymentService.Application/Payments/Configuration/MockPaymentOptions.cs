using System.ComponentModel.DataAnnotations;

namespace OrderHub.PaymentService.Application.Payments.Configuration;

/// <summary>
/// <c>MockPaymentProcessor</c> ayarları: ödemelerin yüzde kaçının başarılı sayılacağı. Gerçek bir gateway
/// yerine geçen deterministik simülasyon içindir. Composition root'ta config'ten bağlanır.
/// </summary>
public sealed class MockPaymentOptions
{
    /// <summary>Configuration bölüm adı.</summary>
    public const string SectionName = "MockPayment";

    /// <summary>Başarı oranı (0–100). 0 = daima başarısız, 100 = daima başarılı (deterministik uçlar).</summary>
    [Range(0, 100)]
    public int SuccessRatePercent { get; init; } = 100;
}
