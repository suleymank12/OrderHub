using Microsoft.Extensions.Logging;

namespace OrderHub.Outbox.Processing;

/// <summary>
/// Outbox processor'ın yapısal log şablonları. <c>[LoggerMessage]</c> source generator → derleme-zamanı,
/// allocation'sız delegeler (CA1848). Mesajlar yalnızca teknik kimlik/tip taşır; payload <b>loglanmaz</b>
/// (PII sızıntısı önlenir, §8). DLQ event'i Error seviyesinde → Seq'te alert kuralına bağlanabilir (§3.8).
/// </summary>
internal static partial class OutboxLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug,
        Message = "Outbox published message {MessageId} of type {MessageType}")]
    public static partial void Published(ILogger logger, Guid messageId, string messageType);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Outbox publish failed for message {MessageId} of type {MessageType}; attempt {RetryCount}")]
    public static partial void PublishFailed(
        ILogger logger, Guid messageId, string messageType, int retryCount, Exception exception);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error,
        Message = "Outbox message {MessageId} of type {MessageType} dead-lettered after {RetryCount} attempt(s); manual intervention required")]
    public static partial void DeadLettered(
        ILogger logger, Guid messageId, string messageType, int retryCount, Exception exception);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error,
        Message = "Outbox processing batch failed; polling loop continues")]
    public static partial void BatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "Outbox publish deferred for message {MessageId} of type {MessageType}; broker unavailable, will retry next poll (RetryCount unchanged)")]
    public static partial void PublishDeferred(
        ILogger logger, Guid messageId, string messageType, Exception exception);
}
