using MediatR;
using OrderHub.Common.Results;

namespace OrderHub.InventoryService.Application.Abstractions.Messaging;

/// <summary>
/// Tüm command'lerin taşıdığı non-generic marker. <c>TransactionBehavior</c> bir request'in command
/// olup olmadığını (commit edilmesi gerekip gerekmediğini) bu interface üzerinden ayırt eder.
/// </summary>
public interface IBaseCommand;

/// <summary>
/// Durum değiştiren (write) bir işlem; başarı durumunda <typeparamref name="TResponse"/> değeri taşır.
/// Daima <c>Result&lt;TResponse&gt;</c> döner — handler beklenen iş sonuçlarını exception fırlatmadan
/// ifade eder (Domain invariant ihlalleri yine de fırlatılır).
/// </summary>
/// <typeparam name="TResponse">Başarı durumunda taşınan değer tipi.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

/// <summary>
/// Durum değiştiren (write) ancak başarı durumunda değer taşımayan işlem. Daima <c>Result</c> döner.
/// InventoryService command'lerinin tamamı bu interface'i kullanır — sipariş bağlamı saga aracılığıyla
/// domain olaylarla izlenir, handler dönüş değerine ihtiyaç duyulmaz.
/// </summary>
public interface ICommand : IRequest<Result>, IBaseCommand;
