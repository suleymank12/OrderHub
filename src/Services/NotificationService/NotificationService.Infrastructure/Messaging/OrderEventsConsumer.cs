using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Application.Abstractions.Scheduling;
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
/// DB commit'ten SONRA yan etki: OrderCreated → reminder zamanla (scheduler opsiyonel), OrderConfirmed → onay e-postası.
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
        // 6c-2: producer'ın inject ettiği W3C traceparent'ı çöz → consume span'ini o trace'e parent'la (Kafka hop
        // trace'i korur; aksi halde yeni root = yarım-trace). İşleme (DB SqlClient span'leri) bu scope altına düşer.
        var parentContext = KafkaTraceContextPropagation.Extract(result.Message.Headers);
        using var activity = KafkaDiagnostics.ActivitySource.StartActivity(
            $"{result.Topic} consume", ActivityKind.Consumer, parentContext);

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

            var processResult = await ProcessAsync(context, typeName, result.Message.Value, cancellationToken);

            switch (processResult.Outcome)
            {
                case ProcessOutcome.UnknownType:
                    OrderEventsLog.UnknownType(logger, typeName);
                    break;
                case ProcessOutcome.AlreadyProcessed:
                    OrderEventsLog.DuplicateSkipped(logger, typeName);
                    break;
                default: // Applied → projection + InboxMessage ATOMİK tek SaveChanges.
                    await context.SaveChangesAsync(cancellationToken); // 1) DB COMMIT
                    await ExecuteSideEffectAsync(scope, processResult.SideEffect, cancellationToken); // 2) e-posta/scheduling
                    break;
            }

            consumer.Commit(result); // 3) ★ DB'den SONRA offset commit (at-least-once).
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

    // DB commit'ten SONRA yan etki: e-posta gönder veya job zamanla. AYNI scope → DbContext request'i kapatmaz.
    // IEmailSender: ZORUNLU (AddInfrastructure her zaman kaydeder). ICartAbandonmentScheduler: OPSİYONEL
    // (Api'de kaydedilir; projection-only testlerde yoktur → GetService null döner, skip + debug log).
    private async Task ExecuteSideEffectAsync(
        IServiceScope scope, SideEffect sideEffect, CancellationToken cancellationToken)
    {
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var scheduler = scope.ServiceProvider.GetService<ICartAbandonmentScheduler>(); // opsiyonel.

        switch (sideEffect.Kind)
        {
            case SideEffectKind.SendConfirmationEmail:
                await emailSender.SendAsync(
                    NotificationEmailKind.OrderConfirmed, sideEffect.CustomerId, sideEffect.OrderId, cancellationToken);
                break;
            case SideEffectKind.ScheduleReminder:
                if (scheduler is null)
                {
                    OrderEventsLog.SchedulerAbsent(logger, sideEffect.OrderId);
                }
                else
                {
                    scheduler.ScheduleReminder(sideEffect.OrderId);
                }
                break;
        }
    }

    // Tip header'a göre dispatch + event-id dedup + projection apply. AnalyticsService'e göre DAHA SADE:
    // Revenue/DailyRevenue YOK → Paid da tek UpdateExistingAsync çağrısıyla halledilir.
    // Her Applied dönüşünde hangi yan etkinin uygulanacağı da kararlaştırılır (SideEffect struct).
    private async Task<ProcessResult> ProcessAsync(
        NotificationDbContext context, string typeName, string json, CancellationToken cancellationToken)
    {
        if (typeName == CreatedType)
        {
            var e = Deserialize<OrderCreatedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return new ProcessResult(ProcessOutcome.AlreadyProcessed, default);
            }

            if (await context.OrderProjections.FindAsync([e.OrderId], cancellationToken) is null)
            {
                context.OrderProjections.Add(OrderProjection.Create(
                    e.OrderId, e.CustomerId, e.Amount, e.Currency, e.OccurredOnUtc, e.OccurredOnUtc));
            }

            Stamp(context, e.Id, typeName);
            OrderEventsLog.Applied(logger, typeName, e.OrderId);
            return new ProcessResult(ProcessOutcome.Applied, new SideEffect(SideEffectKind.ScheduleReminder, e.OrderId, default));
        }

        if (typeName == ConfirmedType)
        {
            var e = Deserialize<OrderConfirmedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return new ProcessResult(ProcessOutcome.AlreadyProcessed, default);
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkConfirmed(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return new ProcessResult(ProcessOutcome.Applied, new SideEffect(SideEffectKind.SendConfirmationEmail, e.OrderId, e.CustomerId));
        }

        if (typeName == PaidType)
        {
            var e = Deserialize<OrderPaidIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return new ProcessResult(ProcessOutcome.AlreadyProcessed, default);
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkPaid(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return new ProcessResult(ProcessOutcome.Applied, default);
        }

        if (typeName == CancelledType)
        {
            var e = Deserialize<OrderCancelledIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, e.Id, typeName, cancellationToken))
            {
                return new ProcessResult(ProcessOutcome.AlreadyProcessed, default);
            }

            await UpdateExistingAsync(context, e.OrderId, p => p.MarkCancelled(e.OccurredOnUtc), typeName, cancellationToken);
            Stamp(context, e.Id, typeName);
            return new ProcessResult(ProcessOutcome.Applied, default);
        }

        return new ProcessResult(ProcessOutcome.UnknownType, default);
    }

    private static async Task<bool> IsDuplicateAsync(
        NotificationDbContext context, Guid eventId, string typeName, CancellationToken cancellationToken) =>
        await context.InboxMessages.AsNoTracking()
            .AnyAsync(m => m.MessageId == eventId && m.MessageType == typeName, cancellationToken);

    private static void Stamp(NotificationDbContext context, Guid eventId, string typeName) =>
        context.InboxMessages.Add(InboxMessage.Create(eventId, typeName));

    private enum ProcessOutcome { Applied, AlreadyProcessed, UnknownType }

    private enum SideEffectKind { None, ScheduleReminder, SendConfirmationEmail }

    private readonly record struct SideEffect(SideEffectKind Kind, Guid OrderId, Guid CustomerId);

    private readonly record struct ProcessResult(ProcessOutcome Outcome, SideEffect SideEffect);

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
