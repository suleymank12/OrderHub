using FluentValidation;
using MediatR;

namespace OrderHub.OrderService.Application.Behaviors;

/// <summary>
/// Handler'a ulaşmadan önce request'i tüm kayıtlı FluentValidation validator'larına karşı doğrular.
/// Hata varsa <see cref="ValidationException"/> fırlatır → Global exception handler (Faz 1.5) bunu 400'e
/// map'ler. Bu, §9 "Result vs exception" konvansiyonunun ihlali değildir: input-contract doğrulaması
/// cross-cutting bir concern'dür ve handler'ın iş sonuçlarından (Result) ayrı bir katmandır.
/// <see cref="TransactionBehavior{TRequest,TResponse}"/>'den önce çalışır → geçersiz input'ta DB açılmaz.
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
