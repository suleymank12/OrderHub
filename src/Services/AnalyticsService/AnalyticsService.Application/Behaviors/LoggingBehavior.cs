using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.AnalyticsService.Application.Common.Logging;
using OrderHub.Common.Results;

namespace OrderHub.AnalyticsService.Application.Behaviors;

/// <summary>
/// Pipeline'ın <b>en dış</b> halkası: her request'in tipini, süresini ve sonucunu yapısal loglar. Request
/// <b>gövdesi loglanmaz</b> → PII log'a düşmez (§8, K3). Sonuç bir <see cref="Result"/> ise başarısızlıkta
/// <c>Error.Code</c> Warning seviyesinde basılır. Her query'de devrededir (read-only API → ölü kod değil).
/// </summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        ApplicationLog.HandlingRequest(logger, requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response is Result { IsFailure: true } failed)
        {
            ApplicationLog.RequestFailed(logger, requestName, failed.Error.Code, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            ApplicationLog.RequestHandled(logger, requestName, stopwatch.ElapsedMilliseconds);
        }

        return response;
    }
}
