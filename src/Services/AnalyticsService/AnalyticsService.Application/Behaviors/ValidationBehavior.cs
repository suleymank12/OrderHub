using FluentValidation;
using MediatR;

namespace OrderHub.AnalyticsService.Application.Behaviors;

/// <summary>
/// Handler'a ulaşmadan önce request'i tüm kayıtlı FluentValidation validator'larına karşı doğrular. Hata
/// varsa <see cref="ValidationException"/> fırlatır → Global exception handler bunu 400'e map'ler. Input-contract
/// doğrulaması cross-cutting concern'dür (Result konvansiyonunun ihlali değil). Read-side'da yalnız anlamlı
/// kontrat sınırlaması olan query'ler validator taşır (ör. <c>GetDailyRevenue</c>: From &lt;= To).
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
