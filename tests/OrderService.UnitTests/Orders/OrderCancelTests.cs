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
    public void Cancel_AlreadyCancelledOrder_IsIdempotentNoOp()
    {
        var order = OrderFactory.CancelledOrder();
        var eventsBefore = order.DomainEvents.OfType<OrderCancelled>().Count();

        var act = () => order.Cancel("again");

        // 5e-1: already-Cancelled → no-op (throw YOK, yeni event yok — MarkPaid/Ship idempotency precedent'i).
        act.Should().NotThrow();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.OfType<OrderCancelled>().Should().HaveCount(eventsBefore, "ikinci kez OrderCancelled yükselmemeli");
    }

    [Fact]
    public void Cancel_PaidOrder_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.PaidOrder();

        var act = () => order.Cancel("too late");

        // Paid iptali refund/compensation gerektirir (kapsam dışı) → throw korunur.
        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void Cancel_ShippedOrder_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.ShippedOrder();

        var act = () => order.Cancel("too late");

        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Shipped);
    }
}
