using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Common.Logging;

namespace OrderHub.OrderService.Application.Behaviors;

/// <summary>
/// Pipeline'ın <b>en dış</b> halkası: her request'in tipini, süresini ve sonucunu yapısal olarak loglar
/// (validation + transaction dahil tüm zinciri ölçer). Request <b>gövdesi loglanmaz</b> → PII (adres,
/// müşteri kimliği) hiç log'a düşmez; maskeleme problemini kaynağında çözer (§8, K3). Sonuç bir
/// <see cref="Result"/> ise başarısızlıkta <c>Error.Code</c> Warning seviyesinde basılır.
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
