using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.UnitTests.ValueObjects;

public sealed class MoneyTests
{
    [Fact]
    public void Create_ValidAmount_SetsAmountAndCurrency()
    {
        var money = Money.Create(99.90m, Currency.USD);

        money.Amount.Should().Be(99.90m);
        money.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void Create_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Money.Create(-1m, Currency.TRY);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Zero_ReturnsZeroAmount()
    {
        Money.Zero(Currency.EUR).Amount.Should().Be(0m);
    }

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        Money.Create(10m, Currency.TRY).Add(Money.Create(5m, Currency.TRY))
            .Should().Be(Money.Create(15m, Currency.TRY));
    }

    [Fact]
    public void Add_DifferentCurrency_ThrowsInvalidOperationException()
    {
        var act = () => Money.Create(10m, Currency.TRY).Add(Money.Create(5m, Currency.USD));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Add_Null_ThrowsArgumentNullException()
    {
        var act = () => Money.Create(10m, Currency.TRY).Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Multiply_PositiveQuantity_MultipliesAmount()
    {
        Money.Create(10m, Currency.TRY).Multiply(3).Should().Be(Money.Create(30m, Currency.TRY));
    }

    [Fact]
    public void Multiply_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Money.Create(10m, Currency.TRY).Multiply(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Sum_MultipleSameCurrency_ReturnsTotal()
    {
        Money.Sum([Money.Create(10m, Currency.TRY), Money.Create(20m, Currency.TRY)])
            .Should().Be(Money.Create(30m, Currency.TRY));
    }

    [Fact]
    public void Sum_Empty_ThrowsInvalidOperationException()
    {
        var act = () => Money.Sum([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sum_Null_ThrowsArgumentNullException()
    {
        var act = () => Money.Sum(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OperatorPlus_SameCurrency_SumsAmounts()
    {
        (Money.Create(10m, Currency.TRY) + Money.Create(5m, Currency.TRY))
            .Should().Be(Money.Create(15m, Currency.TRY));
    }

    [Fact]
    public void OperatorMultiply_MultipliesAmount()
    {
        (Money.Create(10m, Currency.TRY) * 3).Should().Be(Money.Create(30m, Currency.TRY));
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        Money.Create(10m, Currency.TRY).Should().Be(Money.Create(10m, Currency.TRY));
    }

    [Fact]
    public void Equality_DifferentScaleSameValue_AreEqual()
    {
        Money.Create(10.0m, Currency.TRY).Should().Be(Money.Create(10.00m, Currency.TRY));
    }
}
