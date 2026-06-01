using MediatR;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Abstractions.Persistence;

namespace OrderHub.OrderService.Application.Behaviors;

/// <summary>
/// Command'lerin commit sınırı: handler başarıyla dönünce <see cref="IUnitOfWork.SaveChangesAsync"/>'i
/// <b>bir kez</b> çağırır (handler'lar SaveChanges çağırmaz → tek commit, double-save riski yok).
/// Query'ler (<see cref="IBaseCommand"/> olmayanlar) atlanır. Handler bir <see cref="Result"/> başarısızlığı
/// dönerse commit edilmez. Tek SaveChanges = tek transaction (EF sarar); explicit <c>BeginTransaction</c>
/// Faz 3'te Outbox + çoklu-write gerçekten gerektirdiğinde eklenir → ad doğru kalır.
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

        // Başarısız iş sonucunda biriken değişiklikleri kalıcılaştırma.
        if (response is Result { IsFailure: true })
        {
            return response;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return response;
    }
}
