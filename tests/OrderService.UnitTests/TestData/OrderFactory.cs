using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>
/// Test verisi üretici (Object Mother): siparişi farklı yaşam döngüsü durumlarında kurar
/// (Pending → Confirmed → Paid → Shipped, ve Cancelled dalı).
/// </summary>
internal static class OrderFactory
{
    public static Order PendingOrder() =>
        Order.Create(Guid.NewGuid(), AddressFactory.Default(), [OrderItemFactory.Default()]);

    public static Order ConfirmedOrder()
    {
        var order = PendingOrder();
        order.Confirm();
        return order;
    }

    public static Order PaidOrder()
    {
        var order = ConfirmedOrder();
        order.MarkPaid();
        return order;
    }

    public static Order ShippedOrder()
    {
        var order = PaidOrder();
        order.Ship();
        return order;
    }

    public static Order CancelledOrder()
    {
        var order = PendingOrder();
        order.Cancel("test-cancellation");
        return order;
    }
}
