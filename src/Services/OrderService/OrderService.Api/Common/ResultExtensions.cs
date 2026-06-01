using Microsoft.AspNetCore.Mvc;
using OrderHub.Common.Results;

namespace OrderHub.OrderService.Api.Common;

/// <summary>
/// Handler'ın döndürdüğü başarısız <see cref="Result"/>'ı RFC 7807 <see cref="ProblemDetails"/> yanıtına
/// çevirir. Bu yol <b>beklenen iş sonuçları</b> içindir (ör. NotFound); invariant/validation exception'ları
/// ise <c>GlobalExceptionHandler</c> ele alır (iki ayrı çeviri yolu, bilinçli).
/// </summary>
internal static class ResultExtensions
{
    public static IActionResult ToProblemResult(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result cannot be converted to a problem response.");
        }

        var statusCode = result.Error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest, // geçersiz girdi
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict, // kaynağın state'i çakışıyor
            _ => StatusCodes.Status400BadRequest
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonFor(statusCode),
            Detail = result.Error.Message
        };
        problemDetails.Extensions["code"] = result.Error.Code; // makine-okunur hata kodu

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }

    private static string ReasonFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "Bad Request"
    };
}
