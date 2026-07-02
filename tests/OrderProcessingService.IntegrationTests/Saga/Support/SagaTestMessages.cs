using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

/// <summary>
/// Saga'yı tetikleyen/besleyen event fabrikaları (UnitTests InMemory harness'ıyla aynı alanlar) — Test B'de
/// gerçek broker'a publish edilir. Amount/Currency (199.50 TRY) ProcessPayment doğrulamasında kullanılır.
/// </summary>
internal static class SagaTestMessages
{
    public static OrderPlacedIntegrationEvent OrderPlaced(Guid orderId, params Guid[] productIds) =>
        new()
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            Amount = 199.50m,
            Currency = "TRY",
            Items = productIds.Select(productId => new OrderPlacedItem(productId, 1)).ToList(),
        };

    public static StockReservedIntegrationEvent StockReserved(Guid orderId, Guid productId) =>
        new()
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = orderId,
            ProductId = productId,
            Quantity = 1,
        };

    public static PaymentSucceededIntegrationEvent PaymentSucceeded(Guid orderId) =>
        new()
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = orderId,
            ExternalTransactionId = "txn-" + orderId.ToString("N"),
        };

    public static StockReservationConfirmedIntegrationEvent StockReservationConfirmed(Guid orderId, Guid productId) =>
        new()
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = orderId,
            ProductId = productId,
        };
}
