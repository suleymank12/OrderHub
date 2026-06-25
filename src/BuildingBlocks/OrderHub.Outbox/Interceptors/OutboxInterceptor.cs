using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderHub.Common.Primitives;
using OrderHub.Outbox.Serialization;
using OrderHub.Outbox.Translation;

namespace OrderHub.Outbox.Interceptors;

/// <summary>
/// <b>PRE-COMMIT</b> (<see cref="SavingChangesAsync"/> / <see cref="SavingChanges"/>) çalışan outbox yazıcı
/// (ADR-0002 Faz 3 Karar 1): aggregate'lerin biriken domain olaylarını registry ile integration olayına
/// çevirir ve <see cref="OutboxMessage"/> olarak <b>aynı transaction</b>'a ekler → dual-write penceresi kapanır.
/// <para>
/// Domain olay listesine <b>yalnızca OKUR</b>, asla <c>ClearDomainEvents</c> çağırmaz (Karar 2): tek
/// temizleyici post-commit <c>DispatchDomainEventsInterceptor</c>'dır; aksi halde Faz 2 in-process zinciri
/// (OrderCreated → Hangfire timeout) sessizce kırılırdı. Platform tek-tip Guid-keyed (mevcut dispatcher ile
/// aynı varsayım). Registry singleton → bu interceptor da singleton kaydedilebilir.
/// </para>
/// </summary>
internal sealed class OutboxInterceptor(OutboxEventRegistry registry) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddOutboxMessages(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Guid-keyed aggregate root'ların biriken domain olayları — YALNIZCA OKU (clear yok, Karar 2).
        var domainEvents = context.ChangeTracker
            .Entries<AggregateRoot<Guid>>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();

        if (domainEvents.Count == 0)
        {
            return;
        }

        foreach (var domainEvent in domainEvents)
        {
            if (!registry.TryTranslate(domainEvent, out var integrationEvent))
            {
                continue; // Integration karşılığı yok → olay yalnız in-process kalır.
            }

            var (type, payload) = OutboxMessageSerializer.Serialize(integrationEvent);
            context.Set<OutboxMessage>().Add(
                OutboxMessage.Create(integrationEvent.Id, type, payload, integrationEvent.OccurredOnUtc));
        }
    }
}
