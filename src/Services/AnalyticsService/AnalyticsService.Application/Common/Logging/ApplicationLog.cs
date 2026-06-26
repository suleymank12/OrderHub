using Microsoft.Extensions.Logging;

namespace OrderHub.AnalyticsService.Application.Common.Logging;

/// <summary>
/// Application katmanının yapısal log şablonları. <c>[LoggerMessage]</c> source generator → derleme-zamanı,
/// allocation'sız delegeler (CA1848). Mesajlar yalnızca kimlik/teknik alan taşır; request gövdesi/PII
/// loglanmaz (§8).
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
}
