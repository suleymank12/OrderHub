using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderHub.Common.Exceptions;
using OrderHub.OrderService.Domain.Orders.Exceptions;

namespace OrderHub.OrderService.Api.ExceptionHandling;

/// <summary>
/// Tüm yakalanmamış exception'ları RFC 7807 <see cref="ProblemDetails"/>'e çeviren global handler
/// (ASP.NET 8 <see cref="IExceptionHandler"/>). Validation/domain exception'ları anlamlı 4xx'e map'ler;
/// beklenmeyenler 500 olur. <b>Üretimde 500 detayı gizlenir</b> (stack/exception mesajı sızmaz),
/// Development'ta görünür. Sunucu log'larında tam detay daima tutulur.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception);
        }
        else
        {
            LogHandled(logger, exception.GetType().Name, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;

        // 500'de üretimde detay gizli (PII/stack sızıntısı yok); 4xx'te anlamlı mesaj client'a yardımcı.
        var detail = statusCode >= StatusCodes.Status500InternalServerError && environment.IsProduction()
            ? "An unexpected error occurred."
            : exception.Message;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    // Exception → (HTTP status, başlık). 400 = geçersiz girdi, 409 = state conflict, 500 = beklenmeyen.
    private static (int StatusCode, string Title) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
        OrderAlreadyConfirmedException => (StatusCodes.Status409Conflict, "Conflict"),
        InvalidOrderStatusTransitionException => (StatusCodes.Status409Conflict, "Conflict"),
        DomainException => (StatusCodes.Status400BadRequest, "Bad Request"),
        _ => (StatusCodes.Status500InternalServerError, "Server Error")
    };

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled exception while processing request")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning,
        Message = "Request failed with {ExceptionType}: {ExceptionMessage}")]
    private static partial void LogHandled(ILogger logger, string exceptionType, string exceptionMessage);
}
