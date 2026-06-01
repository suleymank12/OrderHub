using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>Test verisi üretici (Object Mother): geçerli <see cref="Money"/> örnekleri.</summary>
internal static class MoneyFactory
{
    public static Money Default() => Money.Create(100m, Currency.TRY);

    public static Money Of(decimal amount, Currency currency = Currency.TRY) => Money.Create(amount, currency);
}
