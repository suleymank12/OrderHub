using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderHub.EventBus;

namespace OrderHub.Inbox.Consuming;

/// <summary>
/// Consumer-side dedup filter (Inbox pattern, ADR-0005). Consumer'dan ÖNCE çalışır: <c>(evt.Id, ConsumerType)</c>
/// daha önce <b>commit edilmişse</b> consumer'ı çağırmaz (skip + ack); değilse <see cref="InboxMessage"/>'ı
/// scoped DbContext'e <b>tracked Add</b> eder ve consumer'ı çağırır — consumer'ın TransactionBehavior
/// <c>SaveChanges</c>'i inbox + iş state + outbox'u <b>tek transaction</b>'da commit eder (Karar 3, açık tx yok).
/// Dedup anahtarı <see cref="IIntegrationEvent.Id"/> (envelope MessageId değil, Karar 2). Concurrency:
/// check-then-add + composite PK backstop (Karar 5). <see cref="IInboxDbContext"/> consumer ile aynı
/// consume-scope DbContext'i olmalı (3d-2 scoped wiring).
/// </summary>
public sealed class InboxConsumeFilter<TConsumer, TMessage>(
    IInboxDbContext inboxDbContext,
    ILogger<InboxConsumeFilter<TConsumer, TMessage>> logger)
    : IFilter<ConsumerConsumeContext<TConsumer, TMessage>>
    where TConsumer : class
    where TMessage : class, IIntegrationEvent
{
    public async Task Send(
        ConsumerConsumeContext<TConsumer, TMessage> context,
        IPipe<ConsumerConsumeContext<TConsumer, TMessage>> next)
    {
        var messageId = context.Message.Id;
        var consumerType = typeof(TConsumer).Name;

        var alreadyProcessed = await inboxDbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.MessageId == messageId && message.ConsumerType == consumerType,
                context.CancellationToken);

        if (alreadyProcessed)
        {
            InboxLog.DuplicateSkipped(logger, messageId, consumerType);
            return; // Consumer'ı çağırma → ack (dedup). next çalışmaz.
        }

        // Tracked Add: consumer'ın tek SaveChanges'i inbox + iş state'i birlikte commit eder (atomik).
        inboxDbContext.InboxMessages.Add(InboxMessage.Create(messageId, consumerType));

        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("inbox");
}
