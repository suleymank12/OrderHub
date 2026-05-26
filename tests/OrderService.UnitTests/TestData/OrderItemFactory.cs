using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>Test verisi üretici (Object Mother): geçerli <see cref="OrderItem"/> örnekleri.</summary>
internal static class OrderItemFactory
{
    public static OrderItem Default() => OrderItem.Create(Guid.NewGuid(), 1, MoneyFactory.Default());

    public static OrderItem WithPrice(decimal amount, Currency currency = Currency.TRY, int quantity = 1) =>
        OrderItem.Create(Guid.NewGuid(), quantity, Money.Create(amount, currency));
}
