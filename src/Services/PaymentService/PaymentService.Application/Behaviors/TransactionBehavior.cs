using MediatR;
using OrderHub.Common.Results;
using OrderHub.PaymentService.Application.Abstractions.Messaging;
using OrderHub.PaymentService.Application.Abstractions.Persistence;

namespace OrderHub.PaymentService.Application.Behaviors;

/// <summary>
/// Command'lerin commit sınırı: handler başarıyla dönünce <see cref="IUnitOfWork.SaveChangesAsync"/>'i
/// <b>bir kez</b> çağırır (handler'lar SaveChanges çağırmaz → tek commit). Query'ler atlanır. Handler bir
/// <see cref="Result"/> başarısızlığı dönerse commit edilmez. Tek SaveChanges = tek transaction (EF sarar);
/// outbox satırı da aynı transaction'da (pre-commit interceptor) → atomik.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IBaseCommand)
        {
            return await next();
        }

        var response = await next();

        if (response is Result { IsFailure: true })
        {
            return response;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return response;
    }
}
