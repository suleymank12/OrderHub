using Microsoft.Extensions.Logging;

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
}
