using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.UnitTests.TestData;

/// <summary>Test verisi üretici (Object Mother): ödemeyi farklı yaşam döngüsü durumlarında kurar.</summary>
internal static class PaymentFactory
{
    public static Payment Pending() => Payment.Create(Guid.NewGuid(), 100m, "TRY");

    public static Payment Succeeded()
    {
        var payment = Pending();
        payment.MarkSucceeded("MOCK-TX-1");
        return payment;
    }

    public static Payment Failed()
    {
        var payment = Pending();
        payment.MarkFailed("declined");
        return payment;
    }
}
