using MediatR;
using OrderHub.Common.Results;

namespace OrderHub.OrderService.Application.Abstractions.Messaging;

/// <summary>
/// Tüm command'lerin taşıdığı non-generic marker. <c>TransactionBehavior</c> bir request'in command
/// olup olmadığını (yani commit edilmesi gerekip gerekmediğini) bu interface üzerinden ayırt eder —
/// generic <see cref="ICommand{TResponse}"/>'i pattern-match etmek mümkün değildir.
/// </summary>
public interface IBaseCommand;

/// <summary>
/// Durum değiştiren (write) bir işlem. Daima <see cref="Result{TResponse}"/> döner → handler beklenen
/// iş sonuçlarını exception fırlatmadan ifade eder (Domain invariant ihlalleri yine de fırlatılır).
/// </summary>
/// <typeparam name="TResponse">Başarı durumunda taşınan değer tipi.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;
