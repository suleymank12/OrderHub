using Microsoft.Extensions.Logging;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Application.Common.Logging;

/// <summary>
/// Application katmanının tüm yapısal log şablonları. <c>[LoggerMessage]</c> source generator'ı ile
/// derleme-zamanı, allocation'sız delegelere dönüşür (CA1848 hot-path kuralı). Şablonların tek yerde
/// toplanması PII sızıntısı denetimini kolaylaştırır (§8): mesajlar yalnızca kimlik/teknik alan taşır,
/// request gövdesi loglanmaz.
/// </summary>
internal static partial class ApplicationLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Handling {RequestName}")]
    public static partial void HandlingRequest(ILogger logger, string requestName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "{RequestName} handled in {ElapsedMilliseconds} ms")]
    public static partial void RequestHandled(ILogger logger, string requestName, long elapsedMilliseconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "{RequestName} completed with failure {ErrorCode} in {ElapsedMilliseconds} ms")]
    public static partial void RequestFailed(
        ILogger logger, string requestName, string errorCode, long elapsedMilliseconds);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Order {OrderId} created for customer {CustomerId}")]
    public static partial void OrderCreated(ILogger logger, Guid orderId, Guid customerId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Cancellation scheduled for order {OrderId} after {Timeout}")]
    public static partial void OrderCancellationScheduled(ILogger logger, Guid orderId, TimeSpan timeout);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "Order {OrderId} cancelled by payment timeout")]
    public static partial void UnpaidOrderCancelled(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug,
        Message = "Cancellation skipped for order {OrderId}; status is {CurrentStatus} (idempotent no-op)")]
    public static partial void UnpaidOrderCancellationSkipped(
        ILogger logger, Guid orderId, OrderStatus? currentStatus);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Sweep cancelled {Count} unpaid order(s) created before {CutoffUtc:o}")]
    public static partial void UnpaidOrdersSwept(ILogger logger, int count, DateTime cutoffUtc);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "Daily sales report {Date}: {Currency} orders={OrderCount} revenue={Revenue}")]
    public static partial void DailySalesReportLine(
        ILogger logger, DateOnly date, string currency, int orderCount, decimal revenue);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "Daily sales report {Date}: no orders")]
    public static partial void DailySalesReportEmpty(ILogger logger, DateOnly date);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information,
        Message = "Low-stock alert skipped: InventoryService not integrated yet (Faz 5)")]
    public static partial void LowStockAlertSkipped(ILogger logger);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information,
        Message = "Order {OrderId} marked paid")]
    public static partial void OrderMarkedPaid(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug,
        Message = "Mark-paid skipped for order {OrderId}; status is {CurrentStatus} (idempotent/edge no-op)")]
    public static partial void OrderMarkPaidSkipped(ILogger logger, Guid orderId, OrderStatus? currentStatus);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Information,
        Message = "Mark-paid deferred for order {OrderId}; still Pending (ConfirmOrder not yet applied) → retryable (Faz 5 saga Karar D)")]
    public static partial void OrderMarkPaidPendingRetry(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information,
        Message = "Order {OrderId} cancelled by payment failure")]
    public static partial void OrderCancelledByPaymentFailure(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug,
        Message = "Payment-failure cancellation skipped for order {OrderId}; status is {CurrentStatus} (idempotent/edge no-op)")]
    public static partial void OrderPaymentFailureSkipped(ILogger logger, Guid orderId, OrderStatus? currentStatus);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Information,
        Message = "Order {OrderId} confirmed")]
    public static partial void OrderConfirmedByCommand(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Debug,
        Message = "Confirm skipped for order {OrderId}; status is {CurrentStatus} (idempotent/edge no-op)")]
    public static partial void OrderConfirmSkipped(ILogger logger, Guid orderId, OrderStatus? currentStatus);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Information,
        Message = "Order {OrderId} shipped")]
    public static partial void OrderShipped(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug,
        Message = "Ship skipped for order {OrderId}; status is {CurrentStatus} (idempotent/edge no-op)")]
    public static partial void OrderShipSkipped(ILogger logger, Guid orderId, OrderStatus? currentStatus);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Information,
        Message = "Order {OrderId} cancelled by saga compensation ({Reason})")]
    public static partial void OrderCancelledByCommand(ILogger logger, Guid orderId, string reason);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Debug,
        Message = "Cancel skipped for order {OrderId}; status is {CurrentStatus} (idempotent/edge no-op)")]
    public static partial void OrderCancelSkipped(ILogger logger, Guid orderId, OrderStatus? currentStatus);
}
