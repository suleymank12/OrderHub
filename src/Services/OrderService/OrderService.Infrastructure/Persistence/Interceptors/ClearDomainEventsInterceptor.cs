using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Başarılı kalıcılaştırma <b>sonrası</b> (commit) aggregate'lerin domain event koleksiyonlarını temizler.
/// Faz 1.4'te tüketici yoktur (in-process dispatch Faz 2, Outbox Faz 3) → bu interceptor şimdilik hijyen +
/// seam görevi görür. Temizleme daima <see cref="SavedChangesAsync"/>'te (post-commit) yapılır; Faz 2'de
/// dispatch eklendiğinde event'ler clear'dan ÖNCE okunabilsin diye timing kritiktir (clear ≥ dispatch).
/// </summary>
internal sealed class ClearDomainEventsInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null)
        {
            // Guid-keyed aggregate root'lar (bu serviste tümü Guid).
            var aggregates = context.ChangeTracker
                .Entries<AggregateRoot<Guid>>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            foreach (var aggregate in aggregates)
            {
                aggregate.ClearDomainEvents();
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
