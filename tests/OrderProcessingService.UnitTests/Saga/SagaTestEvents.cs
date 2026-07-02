using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.UnitTests.Saga;

/// <summary>
/// Saga davranış testleri (happy + compensation) için ortak event fabrikaları. Amount/Currency = 199.50/TRY
/// (ProcessPayment doğrulaması). <c>using static</c> ile çağrılır → test gövdeleri sade kalır.
/// </summary>
internal static class SagaTestEvents
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
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ProductId = productId, Quantity = 1 };

    public static PaymentSucceededIntegrationEvent PaymentSucceeded(Guid orderId) =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ExternalTransactionId = "txn-" + orderId.ToString("N") };

    public static StockReservationConfirmedIntegrationEvent StockReservationConfirmed(Guid orderId, Guid productId) =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ProductId = productId };

    public static StockReservationFailedIntegrationEvent StockReservationFailed(Guid orderId, Guid productId, string reason = "insufficient stock") =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ProductId = productId, Reason = reason };

    public static StockReservationExpiredIntegrationEvent StockReservationExpired(Guid orderId, Guid productId) =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ProductId = productId, Quantity = 1 };

    public static PaymentFailedIntegrationEvent PaymentFailed(Guid orderId, string reason = "card declined") =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, Reason = reason };

    public static StockReleasedIntegrationEvent StockReleased(Guid orderId, Guid productId) =>
        new() { Id = NewId.NextGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId, ProductId = productId, Quantity = 1 };
}
