using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.Domain.ValueObjects;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderCreateTests
{
    [Fact]
    public void Create_ValidItems_StatusIsPending()
    {
        OrderFactory.PendingOrder().Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Create_ValidItems_RaisesOrderCreatedWithPayload()
    {
        var customerId = Guid.NewGuid();

        var order = Order.Create(
            customerId,
            AddressFactory.Default(),
            [OrderItemFactory.WithPrice(50m, Currency.TRY, 2)]);

        var created = order.DomainEvents.OfType<OrderCreated>().Should().ContainSingle().Which;
        created.OrderId.Should().Be(order.Id);
        created.CustomerId.Should().Be(customerId);
        created.Total.Should().Be(order.Total);

        // Faz 5 additive: kalemler saga rezervasyonu için olayda taşınır (ProductId + Quantity).
        var orderItem = order.Items.Should().ContainSingle().Which;
        created.Items.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { orderItem.ProductId, orderItem.Quantity });
    }

    [Fact]
    public void Create_PopulatesCustomerItemsAndAddress()
    {
        var customerId = Guid.NewGuid();
        var address = AddressFactory.Default();
        var item = OrderItemFactory.Default();

        var order = Order.Create(customerId, address, [item]);

        order.CustomerId.Should().Be(customerId);
        order.ShippingAddress.Should().Be(address);
        order.Items.Should().ContainSingle().Which.Should().BeSameAs(item);
    }

    [Fact]
    public void Create_ComputesTotalFromSubtotals()
    {
        var order = Order.Create(
            Guid.NewGuid(),
            AddressFactory.Default(),
            [OrderItemFactory.WithPrice(50m, Currency.TRY, 2), OrderItemFactory.WithPrice(30m, Currency.TRY, 1)]);

        order.Total.Should().Be(Money.Create(130m, Currency.TRY));
    }

    [Fact]
    public void Create_SetsCreatedAtUtc()
    {
        OrderFactory.PendingOrder().CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_EmptyItems_ThrowsEmptyOrderException()
    {
        var act = () => Order.Create(Guid.NewGuid(), AddressFactory.Default(), []);

        act.Should().Throw<EmptyOrderException>();
    }

    [Fact]
    public void Create_NullItems_ThrowsArgumentNullException()
    {
        var act = () => Order.Create(Guid.NewGuid(), AddressFactory.Default(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_EmptyCustomerId_ThrowsArgumentException()
    {
        var act = () => Order.Create(Guid.Empty, AddressFactory.Default(), [OrderItemFactory.Default()]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullShippingAddress_ThrowsArgumentNullException()
    {
        var act = () => Order.Create(Guid.NewGuid(), null!, [OrderItemFactory.Default()]);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_MixedCurrencyItems_ThrowsInvalidOperationException()
    {
        var act = () => Order.Create(
            Guid.NewGuid(),
            AddressFactory.Default(),
            [OrderItemFactory.WithPrice(50m, Currency.TRY), OrderItemFactory.WithPrice(30m, Currency.USD)]);

        act.Should().Throw<InvalidOperationException>();
    }
}
