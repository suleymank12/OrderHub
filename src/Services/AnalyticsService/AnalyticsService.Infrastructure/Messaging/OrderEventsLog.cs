using Microsoft.Extensions.Logging;

namespace OrderHub.AnalyticsService.Infrastructure.Messaging;

/// <summary>
/// <see cref="OrderEventsConsumer"/>'ın yapısal log şablonları (<c>[LoggerMessage]</c> source-gen — derleme-zamanı,
/// allocation'sız). Payload loglanmaz (PII, §8); yalnız tip/offset/anomali sinyalleri.
/// </summary>
internal static partial class OrderEventsLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Debug,
        Message = "Applied {MessageType} for order {OrderId}")]
    public static partial void Applied(ILogger logger, string messageType, Guid orderId);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning,
        Message = "Order projection not found for {MessageType} (order {OrderId}); event skipped (ordering anomaly)")]
    public static partial void ProjectionMissing(ILogger logger, string messageType, Guid orderId);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Error,
        Message = "Kafka message at {Offset} has no message-type header; skipped (offset committed to avoid poison loop)")]
    public static partial void MissingTypeHeader(ILogger logger, string offset);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Error,
        Message = "Unknown Kafka message type '{MessageType}'; skipped (offset committed)")]
    public static partial void UnknownType(ILogger logger, string messageType);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Error,
        Message = "Failed to deserialize Kafka message of type '{MessageType}'; skipped (poison, offset committed)")]
    public static partial void DeserializeFailed(ILogger logger, string messageType, Exception exception);

    [LoggerMessage(EventId = 6005, Level = LogLevel.Error,
        Message = "Failed to process Kafka message '{MessageType}' at {Offset}; will retry (offset NOT committed)")]
    public static partial void ProcessFailed(ILogger logger, string messageType, string offset, Exception exception);

    [LoggerMessage(EventId = 6006, Level = LogLevel.Debug,
        Message = "Duplicate {MessageType} (event-id already processed); projection/revenue unchanged, offset committed")]
    public static partial void DuplicateSkipped(ILogger logger, string messageType);

    [LoggerMessage(EventId = 6007, Level = LogLevel.Warning,
        Message = "Order events topic unavailable ({ErrorCode}); subscription stays live, waiting for topic to appear (self-heal)")]
    public static partial void TopicUnavailable(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 6008, Level = LogLevel.Error,
        Message = "Kafka consume error ({ErrorCode}); consumer stays alive, retrying after backoff")]
    public static partial void ConsumeError(ILogger logger, string errorCode, Exception exception);
}
