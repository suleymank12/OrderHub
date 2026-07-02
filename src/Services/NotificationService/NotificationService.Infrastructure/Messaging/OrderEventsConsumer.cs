using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.NotificationService.Domain.Orders;
using OrderHub.NotificationService.Infrastructure.Persistence;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.Inbox;

namespace OrderHub.NotificationService.Infrastructure.Messaging;

/// <summary>
/// Kafka order-stream consumer (ADR-0006). <c>order-hub.orders.events</c>'i tüketip
/// <see cref="OrderProjection"/> read-model'ini günceller. <b>At-least-once:</b> her mesaj işlenir → DB commit →
/// <b>SONRA</b> offset commit. Tip header'dan (<see cref="KafkaMessageHeaders.MessageType"/>) dispatch eder.
/// <c>enable.auto.commit=false</c>, manual <c>Commit</c>. Singleton consumer → scoped
/// <see cref="NotificationDbContext"/> her mesajda <see cref="IServiceScopeFactory"/> ile yeni scope'tan resolve.
/// Revenue YOK (AnalyticsService'e göre daha sade); yalnız projection lifecycle + inbox dedup.
/// </summary>
internal sealed class OrderEventsConsumer(
    IConsumer<string, string> consumer,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderEventsConsumer> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConsumeErrorBackoff = TimeSpan.FromSeconds(2);

    // Fresh-stack'te beklenen geçici broker hataları: topic henüz yok / leader seçilmedi.
    // librdkafka subscription'ı canlı tutar → topic gelince re-subscribe gerekmeden fetch başlar (self-heal).
    private static readonly HashSet<ErrorCode> TransientStartupCodes =
    [
        ErrorCode.UnknownTopicOrPart,
        ErrorCode.Local_UnknownTopic,
        ErrorCode.LeaderNotAvailable,
    ];

    private static readonly string CreatedType = typeof(OrderCreatedIntegrationEvent).FullName!;
    private static readonly string ConfirmedType = typeof(OrderConfirmedIntegrationEvent).FullName!;
    private static readonly string PaidType = typeof(OrderPaidIntegrationEvent).FullName!;
    private static readonly string CancelledType = typeof(OrderCancelledIntegrationEvent).FullName!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Senkron Consume host startup'ı bloklamasın → loop continuation'da koşar.

        consumer.Subscribe(OrderStreamEvent.OrdersTopic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; // graceful shutdown.
                }
                catch (ConsumeException consumeException)
                {
                    // Consume-SEVİYESİ hata: mesaj ALINAMADI. HandleAsync mesaj-seviyesi hatasından AYRI katman.
                    if (await TryHandleConsumeErrorAsync(consumeException, stoppingToken))
                    {
                        continue;
                    }

                    throw; // fatal: kurtarılamaz → host crash → orchestrator restart.
                }

                if (result?.Message is not null)
                {
                    await HandleAsync(result, stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close(); // final offset commit + partition release + group leave.
        }
    }

    // true → geçici (logla + backoff), çağıran continue eder. false → fatal, çağıran rethrow eder.
    private async Task<bool> TryHandleConsumeErrorAsync(ConsumeException exception, CancellationToken stoppingToken)
    {
        if (exception.Error.IsFatal)
        {
            return false;
        }

        var code = exception.Error.Code;
        if (TransientStartupCodes.Contains(code))
        {
            // Normal fresh-stack durumu: topic henüz yok. Warning (Error değil) — anomali değil.
            OrderEventsLog.TopicUnavailable(logger, code.ToString());
        }
        else
        {
            // Diğer non-fatal: consumer hayatta kalsın ama Error seviyesinde GÖRÜNÜR olsun.
            OrderEventsLog.ConsumeError(logger, code.ToString(), exception);
        }

        try
        {
            await Task.Delay(ConsumeErrorBackoff, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown sırasında backoff iptal edildi → continue; dış while stoppingToken'ı görüp Close'a gider.
        }

        return true;
    }

    private async Task HandleAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        if (!result.Message.Headers.TryGetLastBytes(KafkaMessageHeaders.MessageType, out var typeBytes))
        {
            OrderEventsLog.MissingTypeHeader(logger, result.TopicPartitionOffset.ToString());
            consumer.Commit(result); // poison → skip (sonsuz re-delivery'i önle).
            return;
        }

        var typeName = Encoding.UTF8.GetString(typeBytes);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

            switch (await ProcessAsync(context, typeName, result.Message.Value, cancellationToken))
            {
                case ProcessOutcome.UnknownType:
                    OrderEventsLog.UnknownType(logger, typeName);
                    break;
                case ProcessOutcome.AlreadyProcessed:
                    OrderEventsLog.DuplicateSkipped(logger, typeName);
                    break;
                default: // Applied → projection + InboxMessage ATOMİK tek SaveChanges.
                    await context.SaveChangesAsync(cancellationToken); // 1) DB COMMIT
                    break;
            }

            consumer.Commit(result); // 2) ★ DB'den SONRA offset commit (at-least-once).
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown → loop break + Close.
        }
        catch (JsonException jsonException)
        {
            OrderEventsLog.DeserializeFailed(logger, typeName, jsonException);
            consumer.Commit(result); // malformed payload → poison skip.
        }
#pragma warning disable CA1031 // Transient (DB down vb.): offset commit ETME → aynı offset'i tekrar oku (at-least-once retry).
        catch (Exception exception)
#pragma warning restore CA1031
        {
            OrderEventsLog.ProcessFailed(logger, typeName, result.TopicPartitionOffset.ToString(), exception);
            consumer.Seek(result.TopicPartitionOffset);
            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    // Tip header'a göre dispatch + event-id dedup + projection apply. AnalyticsService'e göre DAHA SADE:
    // Revenue/DailyRevenue YOK → Paid da tek UpdateExistingAsync çağrısıyla halledilir.
    private async Task<ProcessOutcome> ProcessAsync(
        NotificationDbContext context, string typeName, string json, CancellationToken cancellationToken)
    {
        if (typeName == CreatedType)
        {
            var e = Deserialize<OrderCreatedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            if (await context.OrderProjections.FindAsync([e.OrderId], cancellationToken) is null)
            {
                context.OrderProjections.Add(OrderProjection.Create(
                    e.OrderId, e.CustomerId, e.Amount, e.Currency, e.OccurredOnUtc, e.OccurredOnUtc));
            }

            Stamp(context, e.Id, typeName);
            OrderEventsLog.Applied(logger, typeName, e.OrderId);
            return ProcessOutcome.Applied;
        }

        if (typeName == ConfirmedType)
        {
            var e = Deserialize<OrderConfirmedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkConfirmed(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return ProcessOutcome.Applied;
        }

        if (typeName == PaidType)
        {
            var e = Deserialize<OrderPaidIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkPaid(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return ProcessOutcome.Applied;
        }

        if (typeName == CancelledType)
        {
            var e = Deserialize<OrderCancelledIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkCancelled(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return ProcessOutcome.Applied;
        }

        return ProcessOutcome.UnknownType;
    }

    private static async Task<bool> IsDuplicateAsync(
        NotificationDbContext context, Guid eventId, string typeName, CancellationToken cancellationToken) =>
        await context.InboxMessages.AsNoTracking()
            .AnyAsync(m => m.MessageId == eventId && m.MessageType == typeName, cancellationToken);

    private static void Stamp(NotificationDbContext context, Guid eventId, string typeName) =>
        context.InboxMessages.Add(InboxMessage.Create(eventId, typeName));

    private enum ProcessOutcome
    {
        Applied,
        AlreadyProcessed,
        UnknownType,
    }

    private async Task UpdateExistingAsync(
        NotificationDbContext context, Guid orderId, Action<OrderProjection> apply, string typeName, CancellationToken cancellationToken)
    {
        var projection = await context.OrderProjections.FindAsync([orderId], cancellationToken);
        if (projection is null)
        {
            // Ordering (partition key=OrderId) Created'ı önce getirir → normalde satır var. Yoksa anomali → logla, skip.
            OrderEventsLog.ProjectionMissing(logger, typeName, orderId);
            return;
        }

        apply(projection);
        OrderEventsLog.Applied(logger, typeName, orderId);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions)
        ?? throw new JsonException($"Kafka payload deserialized to null for type '{typeof(T).Name}'.");
}
