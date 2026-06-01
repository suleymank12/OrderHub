using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.IntegrationTests.TestData;

/// <summary>
/// Integration test'ler için geçerli <see cref="Order"/> üretici (Object Mother). Domain factory'leri
/// üzerinden kurar → invariant'lara saygılı. Her çağrı yeni kimlikler üretir (test izolasyonu).
/// </summary>
internal static class OrderTestData
{
    public static Order NewOrder() =>
        Order.Create(
            Guid.NewGuid(),
            Address.Create("123 Main St", "Istanbul", "34000", "Türkiye"),
            [OrderItem.Create(Guid.NewGuid(), 2, Money.Create(50m, Currency.TRY))]);
}
