using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderHub.Common.Primitives;
using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Başarılı kalıcılaştırma <b>sonrası</b> (post-commit) aggregate'lerin domain olaylarını in-process yayımlar:
/// her <see cref="IDomainEvent"/>, <see cref="DomainEventNotification{TDomainEvent}"/> ile sarılıp MediatR
/// <see cref="IPublisher"/> üzerinden publish edilir, <b>ardından</b> olay listeleri temizlenir
/// (clear ≥ dispatch → çift yayın olmaz). Post-commit seçimi (ADR-0002): handler exception'ı veriyi rollback
/// etmez (commit zaten olmuştur). Domain MediatR'a bağlanmaz; INotification adaptasyonu burada (Infrastructure)
/// yapılır. <see cref="IPublisher"/> scoped olduğundan bu interceptor da scoped kaydedilir.
/// </summary>
internal sealed class DispatchDomainEventsInterceptor(IPublisher publisher) : SaveChangesInterceptor
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

            // Önce tüm olayları topla; publish handler'ları ChangeTracker'ı değiştirebilir.
            var domainEvents = aggregates
                .SelectMany(aggregate => aggregate.DomainEvents)
                .ToList();

            foreach (var domainEvent in domainEvents)
            {
                await publisher.Publish(WrapAsNotification(domainEvent), cancellationToken);
            }

            // Yayın sonrası temizle (clear ≥ dispatch): yeniden SaveChanges olsa bile çift yayın olmaz.
            foreach (var aggregate in aggregates)
            {
                aggregate.ClearDomainEvents();
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// <see cref="IDomainEvent"/> (INotification değil) → kapalı generic
    /// <see cref="DomainEventNotification{TDomainEvent}"/> (INotification). Somut tip yalnızca runtime'da
    /// bilindiğinden reflection ile sarılır; <c>dynamic</c> kullanılmaz (§3).
    /// </summary>
    private static INotification WrapAsNotification(IDomainEvent domainEvent)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        return (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
    }
}
