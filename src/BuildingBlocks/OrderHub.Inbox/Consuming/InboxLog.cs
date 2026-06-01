using Microsoft.Extensions.Logging;

namespace OrderHub.Inbox.Consuming;

/// <summary>
/// Inbox filter'ın yapısal log şablonları. <c>[LoggerMessage]</c> source generator → allocation'sız (CA1848).
/// Yalnızca teknik kimlik taşır (PII yok, §8).
/// </summary>
internal static partial class InboxLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Debug,
        Message = "Inbox duplicate skipped: message {MessageId} already processed by {ConsumerType}")]
    public static partial void DuplicateSkipped(ILogger logger, Guid messageId, string consumerType);
}
