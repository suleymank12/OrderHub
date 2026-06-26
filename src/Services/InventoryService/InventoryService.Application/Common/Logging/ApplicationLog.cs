using Microsoft.Extensions.Logging;

namespace OrderHub.InventoryService.Application.Common.Logging;

/// <summary>
/// Application katmanının yapısal log şablonları. <c>[LoggerMessage]</c> source generator → derleme-zamanı,
/// allocation'sız delegeler (CA1848). Mesajlar yalnızca kimlik/teknik alan taşır; request gövdesi/PII
/// loglanmaz (§8).
/// </summary>
internal static partial class ApplicationLog
{
    // ── Pipeline behavior'ları (7000-7002) ──────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 7000, Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    public static partial void HandlingRequest(ILogger logger, string requestName);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information,
        Message = "{RequestName} handled in {ElapsedMilliseconds} ms")]
    public static partial void RequestHandled(ILogger logger, string requestName, long elapsedMilliseconds);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
        Message = "{RequestName} completed with failure {ErrorCode} in {ElapsedMilliseconds} ms")]
    public static partial void RequestFailed(
        ILogger logger, string requestName, string errorCode, long elapsedMilliseconds);

    // ── Handler domain log'ları (7100-7105) ─────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information,
        Message = "Stock reserved for order {OrderId}, product {ProductId}, quantity {Quantity}")]
    public static partial void StockReserved(ILogger logger, Guid orderId, Guid productId, int quantity);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning,
        Message = "Stock reservation failed for order {OrderId}, product {ProductId}: {Reason}")]
    public static partial void StockReservationFailed(ILogger logger, Guid orderId, Guid productId, string reason);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Information,
        Message = "Stock reservation confirmed for order {OrderId}, product {ProductId}")]
    public static partial void StockReservationConfirmed(ILogger logger, Guid orderId, Guid productId);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Information,
        Message = "Stock released for order {OrderId}, product {ProductId}")]
    public static partial void StockReleased(ILogger logger, Guid orderId, Guid productId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Warning,
        Message = "Stock item not found for order {OrderId}, product {ProductId}")]
    public static partial void StockItemNotFound(ILogger logger, Guid orderId, Guid productId);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Information,
        Message = "{Count} expired stock reservation(s) processed")]
    public static partial void StockReservationsExpired(ILogger logger, int count);
}
