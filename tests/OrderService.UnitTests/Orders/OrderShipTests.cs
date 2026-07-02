using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderShipTests
{
    [Fact]
    public void Ship_FromPaid_TransitionsToShipped()
    {
        var order = OrderFactory.PaidOrder();

        order.Ship();

        order.Status.Should().Be(OrderStatus.Shipped);
        order.ShippedAtUtc.Should().NotBeNull();
        order.ShippedAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Ship_FromPaid_RaisesOrderShipped()
    {
        var order = OrderFactory.PaidOrder();

        order.Ship();

        order.DomainEvents.OfType<OrderShipped>().Should().ContainSingle()
            .Which.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public void Ship_AlreadyShipped_IsNoOpAndDoesNotThrow()
    {
        var order = OrderFactory.ShippedOrder();
        var shippedAt = order.ShippedAtUtc;

        var act = () => order.Ship();

        // Idempotent: throw DEĞİL, no-op. Status/zaman değişmez, ikinci OrderShipped yükselmez.
        act.Should().NotThrow();
        order.Status.Should().Be(OrderStatus.Shipped);
        order.ShippedAtUtc.Should().Be(shippedAt);
        order.DomainEvents.OfType<OrderShipped>().Should().ContainSingle("ikinci Ship yeni event yükseltmemeli");
    }

    [Fact]
    public void Ship_FromConfirmed_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.ConfirmedOrder();

        var act = () => order.Ship();

        var exception = act.Should().Throw<InvalidOrderStatusTransitionException>().Which;
        exception.From.Should().Be(OrderStatus.Confirmed);
        exception.To.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public void Ship_FromPending_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.PendingOrder();

        var act = () => order.Ship();

        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Ship_FromCancelled_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.CancelledOrder();

        var act = () => order.Ship();

        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Cancelled);
    }
}
