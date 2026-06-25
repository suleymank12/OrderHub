using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderHub.EventBus;

namespace OrderHub.Inbox.Consuming;

/// <summary>
/// Consumer-side dedup filter (Inbox pattern, ADR-0005). <b>Message-level</b> (Karar 6): MassTransit'in
/// scoped open-generic <c>UseConsumeFilter(typeof(InboxConsumeFilter&lt;&gt;), context)</c> yardımcısı yalnız
/// message-level'ı destekler. Consumer dispatch'inden ÖNCE çalışır: <c>(evt.Id, MessageType)</c> daha önce
/// <b>commit edilmişse</b> consumer'ı çağırmaz (skip + ack); değilse <see cref="InboxMessage"/>'ı scoped
/// DbContext'e <b>tracked Add</b> eder ve devam eder — consumer'ın TransactionBehavior <c>SaveChanges</c>'i
/// inbox + iş state + outbox'u <b>tek transaction</b>'da commit eder (Karar 3, açık tx yok). Dedup anahtarı
/// <see cref="IIntegrationEvent.Id"/> (envelope MessageId değil, Karar 2). Concurrency: check-then-add +
/// composite PK backstop (Karar 5). <see cref="IInboxDbContext"/> consumer ile aynı consume-scope DbContext'i olmalı.
/// </summary>
public sealed class InboxConsumeFilter<TMessage>(
    IInboxDbContext inboxDbContext,
    ILogger<InboxConsumeFilter<TMessage>> logger)
    : IFilter<ConsumeContext<TMessage>>
    where TMessage : class, IIntegrationEvent
{
    private static readonly string MessageTypeName = typeof(TMessage).FullName!;

    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var messageId = context.Message.Id;

        var alreadyProcessed = await inboxDbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.MessageId == messageId && message.MessageType == MessageTypeName,
                context.CancellationToken);

        if (alreadyProcessed)
        {
            InboxLog.DuplicateSkipped(logger, messageId, MessageTypeName);
            return; // Consumer dispatch'i çalışmaz → ack (dedup). next çağrılmaz.
        }

        // Tracked Add: consumer'ın tek SaveChanges'i inbox + iş state'i birlikte commit eder (atomik).
        inboxDbContext.InboxMessages.Add(InboxMessage.Create(messageId, MessageTypeName));

        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("inbox");
}
