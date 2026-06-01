using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders;

public sealed class OrderConfirmTests
{
    [Fact]
    public void Confirm_PendingOrder_TransitionsToConfirmed()
    {
        var order = OrderFactory.PendingOrder();

        order.Confirm();

        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Confirm_PendingOrder_RaisesOrderConfirmed()
    {
        var order = OrderFactory.PendingOrder();

        order.Confirm();

        var confirmed = order.DomainEvents.OfType<OrderConfirmed>().Should().ContainSingle().Which;
        confirmed.OrderId.Should().Be(order.Id);
        // Faz 3: ödeme köprülemesi için CustomerId + Total taşınmalı (outbox çevirisi saf kalsın).
        confirmed.CustomerId.Should().Be(order.CustomerId);
        confirmed.Total.Should().Be(order.Total);
    }

    [Fact]
    public void Confirm_PendingOrder_SetsConfirmedAtUtc()
    {
        var order = OrderFactory.PendingOrder();

        order.Confirm();

        order.ConfirmedAtUtc.Should().NotBeNull();
        order.ConfirmedAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Confirm_AlreadyConfirmedOrder_ThrowsOrderAlreadyConfirmedException()
    {
        var order = OrderFactory.ConfirmedOrder();

        var act = () => order.Confirm();

        act.Should().Throw<OrderAlreadyConfirmedException>()
            .Which.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public void Confirm_CancelledOrder_ThrowsInvalidOrderStatusTransitionException()
    {
        var order = OrderFactory.CancelledOrder();

        var act = () => order.Confirm();

        var exception = act.Should().Throw<InvalidOrderStatusTransitionException>().Which;
        exception.From.Should().Be(OrderStatus.Cancelled);
        exception.To.Should().Be(OrderStatus.Confirmed);
    }
}
