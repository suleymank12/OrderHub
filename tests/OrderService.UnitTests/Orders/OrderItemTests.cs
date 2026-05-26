using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderItemTests
{
    [Fact]
    public void Create_ValidArguments_SetsProperties()
    {
        var productId = Guid.NewGuid();
        var unitPrice = Money.Create(50m, Currency.TRY);

        var item = OrderItem.Create(productId, 2, unitPrice);

        item.ProductId.Should().Be(productId);
        item.Quantity.Should().Be(2);
        item.UnitPrice.Should().Be(unitPrice);
    }

    [Fact]
    public void Subtotal_IsUnitPriceTimesQuantity()
    {
        var item = OrderItem.Create(Guid.NewGuid(), 3, Money.Create(50m, Currency.TRY));

        item.Subtotal.Should().Be(Money.Create(150m, Currency.TRY));
    }

    [Fact]
    public void Create_EmptyProductId_ThrowsArgumentException()
    {
        var act = () => OrderItem.Create(Guid.Empty, 1, MoneyFactory.Default());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ZeroQuantity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => OrderItem.Create(Guid.NewGuid(), 0, MoneyFactory.Default());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => OrderItem.Create(Guid.NewGuid(), -1, MoneyFactory.Default());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_NullUnitPrice_ThrowsArgumentNullException()
    {
        var act = () => OrderItem.Create(Guid.NewGuid(), 1, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
