using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderMarkPaidTests
{
    [Fact]
    public void MarkPaid_FromConfirmed_TransitionsToPaid()
    {
        var order = OrderFactory.ConfirmedOrder();

        order.MarkPaid();

        order.Status.Should().Be(OrderStatus.Paid);
        order.PaidAtUtc.Should().NotBeNull();
        order.PaidAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkPaid_FromConfirmed_RaisesOrderPaid()
    {
        var order = OrderFactory.ConfirmedOrder();

        order.MarkPaid();

        order.DomainEvents.OfType<OrderPaid>().Should().ContainSingle()
            .Which.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public void MarkPaid_AlreadyPaid_IsNoOpAndDoesNotThrow()
    {
        var order = OrderFactory.PaidOrder();
        var paidAt = order.PaidAtUtc;

        var act = () => order.MarkPaid();

        // Idempotent: throw DEĞİL, no-op. Status/zaman değişmez, ikinci OrderPaid yükselmez.
        act.Should().NotThrow();
        order.Status.Should().Be(OrderStatus.Paid);
        order.PaidAtUtc.Should().Be(paidAt);
        order.DomainEvents.OfType<OrderPaid>().Should().ContainSingle("ikinci MarkPaid yeni event yükseltmemeli");
    }

    [Fact]
    public void MarkPaid_FromPending_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.PendingOrder();

        var act = () => order.MarkPaid();

        var exception = act.Should().Throw<InvalidOrderStatusTransitionException>().Which;
        exception.From.Should().Be(OrderStatus.Pending);
        exception.To.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void MarkPaid_FromCancelled_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.CancelledOrder();

        var act = () => order.MarkPaid();

        act.Should().Throw<InvalidOrderStatusTransitionException>()
            .Which.From.Should().Be(OrderStatus.Cancelled);
    }
}
