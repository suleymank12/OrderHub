using MediatR;
using OrderHub.Common.Results;

namespace OrderHub.PaymentService.Application.Abstractions.Messaging;

/// <summary>
/// Durum değiştirmeyen (read) bir işlem. Daima <see cref="Result{TResponse}"/> döner; ör. kayıt
/// bulunamazsa handler <c>Result.Failure(Error.NotFound(...))</c> verir (404 beklenen sonuç → exception
/// anti-pattern). Query'ler <see cref="IBaseCommand"/> taşımaz → TransactionBehavior onları commit etmez.
/// </summary>
/// <typeparam name="TResponse">Başarı durumunda taşınan değer tipi.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
