using Microsoft.Extensions.Options;
using OrderHub.PaymentService.Application.Payments;
using OrderHub.PaymentService.Application.Payments.Configuration;

namespace OrderHub.PaymentService.UnitTests.Payments;

/// <summary>
/// Mock sağlayıcının <b>deterministik uçları</b>: %100 → daima başarı, %0 → daima başarısızlık.
/// (Ara oranlar olasılıksaldır; testler yalnızca deterministik uçları doğrular.)
/// </summary>
public sealed class MockPaymentProcessorTests
{
    private static MockPaymentProcessor Processor(int successRatePercent) =>
        new(Options.Create(new MockPaymentOptions { SuccessRatePercent = successRatePercent }));

    [Fact]
    public async Task ProcessAsync_HundredPercentSuccessRate_AlwaysSucceeds()
    {
        var processor = Processor(100);

        var result = await processor.ProcessAsync(Guid.NewGuid(), 100m, "TRY", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ExternalTransactionId.Should().NotBeNullOrWhiteSpace();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_ZeroPercentSuccessRate_AlwaysFails()
    {
        var processor = Processor(0);

        var result = await processor.ProcessAsync(Guid.NewGuid(), 100m, "TRY", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ExternalTransactionId.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }
}
