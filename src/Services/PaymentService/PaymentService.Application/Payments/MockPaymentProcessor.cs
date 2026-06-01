using Microsoft.Extensions.Options;
using OrderHub.PaymentService.Application.Abstractions.Payments;
using OrderHub.PaymentService.Application.Payments.Configuration;

namespace OrderHub.PaymentService.Application.Payments;

/// <summary>
/// Gerçek ödeme sağlayıcısı yerine geçen deterministik mock (Faz 3). Yapılandırılan
/// <see cref="MockPaymentOptions.SuccessRatePercent"/> oranına göre başarı/başarısızlık üretir; uçlar
/// (%0/%100) tamamen deterministiktir (testler bunları kullanır). I/O yoktur → saf Application stand-in;
/// ileride gerçek HTTP adapter aynı <see cref="IPaymentProcessor"/> port'unun arkasına geçer.
/// </summary>
internal sealed class MockPaymentProcessor(IOptions<MockPaymentOptions> options) : IPaymentProcessor
{
    private readonly MockPaymentOptions _options = options.Value;

    public Task<PaymentResult> ProcessAsync(
        Guid orderId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        // NextDouble() ∈ [0,1) → *100 ∈ [0,100). %100'de daima true, %0'da daima false (deterministik uçlar).
        var roll = Random.Shared.NextDouble() * 100;
        var isSuccess = roll < _options.SuccessRatePercent;

        var result = isSuccess
            ? new PaymentResult(true, $"MOCK-{Guid.NewGuid():N}", null)
            : new PaymentResult(false, null, "Payment was declined by the provider.");

        return Task.FromResult(result);
    }
}
