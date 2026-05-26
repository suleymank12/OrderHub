using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>
/// Test verisi üretici (Object Mother): siparişi farklı yaşam döngüsü durumlarında kurar.
/// Paid/Shipped'e ulaşmanın yolu Faz 1'de yok (Pay/Ship Faz 3/5); guard'lar Cancelled ile test edilir.
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

    public static Order CancelledOrder()
    {
        var order = PendingOrder();
        order.Cancel("test-cancellation");
        return order;
    }
}
