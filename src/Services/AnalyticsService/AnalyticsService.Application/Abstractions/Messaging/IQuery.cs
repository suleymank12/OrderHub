using MediatR;
using OrderHub.Common.Results;

namespace OrderHub.AnalyticsService.Application.Abstractions.Messaging;

/// <summary>
/// Durum değiştirmeyen (read) bir işlem. Daima <see cref="Result{TResponse}"/> döner; ör. kayıt bulunamazsa
/// handler <c>Result.Failure(Error.NotFound(...))</c> verir (404 beklenen sonuç → exception anti-pattern).
/// AnalyticsService read-only'dir → yalnız query vardır, command yoktur (bu yüzden TransactionBehavior de yok).
/// </summary>
/// <typeparam name="TResponse">Başarı durumunda taşınan değer tipi.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
