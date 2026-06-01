using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderCancelTests
{
    [Fact]
    public void Cancel_PendingOrder_TransitionsToCancelled()
    {
        var order = OrderFactory.PendingOrder();

        order.Cancel("customer request");

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_ConfirmedOrder_TransitionsToCancelled()
    {
        var order = OrderFactory.ConfirmedOrder();

        order.Cancel("customer request");

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_PendingOrder_RaisesOrderCancelledWithReason()
    {
        var order = OrderFactory.PendingOrder();

        order.Cancel("out of stock");

        order.DomainEvents.OfType<OrderCancelled>().Should().ContainSingle()
            .Which.Reason.Should().Be("out of stock");
    }

    [Fact]
    public void Cancel_SetsCancelledAtUtcAndTrimmedReason()
    {
        var order = OrderFactory.PendingOrder();

        order.Cancel("  fraud  ");

        order.CancellationReason.Should().Be("fraud");
        order.CancelledAtUtc.Should().NotBeNull();
        order.CancelledAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Cancel_EmptyReason_ThrowsArgumentException()
    {
        var order = OrderFactory.PendingOrder();

        var act = () => order.Cancel("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cancel_WhitespaceReason_ThrowsArgumentException()
    {
        var order = OrderFactory.PendingOrder();

        var act = () => order.Cancel("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cancel_AlreadyCancelledOrder_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.CancelledOrder();

        var act = () => order.Cancel("again");

        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Cancelled);
    }
}
