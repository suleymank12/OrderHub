using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.AnalyticsService.Infrastructure.Persistence;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;
using OrderHub.Inbox;

namespace OrderHub.AnalyticsService.Infrastructure.Messaging;

/// <summary>
/// Kafka order-stream consumer (ADR-0006, ROADMAP §4.4). <c>order-hub.orders.events</c>'i tüketip
/// <see cref="OrderProjection"/> read-model'ini günceller. <b>At-least-once:</b> her mesaj işlenir → DB commit →
/// <b>SONRA</b> offset commit (DB önce, offset sonra). Tip header'dan (<see cref="KafkaMessageHeaders.MessageType"/>)
/// dispatch eder (value JSON şema taşımaz). <c>enable.auto.commit=false</c>, manual <c>Commit</c>. Singleton →
/// scoped <see cref="AnalyticsDbContext"/> her mesajda <see cref="IServiceScopeFactory"/> ile yeni scope'tan resolve.
/// ★ İdempotency (event-id dedup) + revenue aggregate 4c-3'te; bu adım OrderProjection lifecycle (happy-path).
/// </summary>
internal sealed class OrderEventsConsumer(
    IConsumer<string, string> consumer,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderEventsConsumer> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1); // transient hatada hot-loop önler.
    private static readonly TimeSpan ConsumeErrorBackoff = TimeSpan.FromSeconds(2); // Consume-seviyesi hata → hot-loop + log spam önler.

    // Consume() sırasında fresh-stack'te beklenen, GEÇİCİ broker hataları: topic henüz yok / leader seçilmedi.
    // librdkafka subscription'ı canlı tutar → topic gelince re-subscribe gerekmeden fetch başlar (self-heal).
    private static readonly HashSet<ErrorCode> TransientStartupCodes =
    [
        ErrorCode.UnknownTopicOrPart, // broker: topic mevcut değil (henüz oluşmadı).
        ErrorCode.Local_UnknownTopic, // librdkafka local: metadata'da topic yok.
        ErrorCode.LeaderNotAvailable, // partition leader seçimi sürüyor (startup).
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
                    // ★ Consume-SEVİYESİ hata: mesaj ALINAMADI (bağlantı/metadata/topic). HandleAsync mesaj-seviyesi
                    // (mesaj alındıktan SONRA) hatasından AYRI katman — karıştırma. Fresh-stack'te topic henüz yok →
                    // ele al + bekle + continue (abonelik canlı, topic gelince fetch başlar → consumer ÖLMEZ).
                    if (await TryHandleConsumeErrorAsync(consumeException, stoppingToken))
                    {
                        continue;
                    }

                    throw; // fatal: librdkafka kurtarılamaz durumda → host crash → orchestrator restart (gizleme yok).
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

    // Confluent client'ın Consume() sırasında fırlattığı broker/bağlantı hatasını sınıflandırır.
    // true → ele alındı (logla + backoff), çağıran continue eder (consumer ayakta kalır).
    // false → fatal (kurtarılamaz) → çağıran rethrow eder (host crash, orchestrator restart).
    private async Task<bool> TryHandleConsumeErrorAsync(ConsumeException exception, CancellationToken stoppingToken)
    {
        // librdkafka FATAL bayrağı: client kurtarılamaz (ör. idempotence violation, auth fatal). Yeni client gerek
        // → sessizce sonsuz retry'da GİZLEME, yukarı fırlat (denge: fatal olmayan her şeyde hayatta kal, fatal'da çık).
        if (exception.Error.IsFatal)
        {
            return false;
        }

        var code = exception.Error.Code;
        if (TransientStartupCodes.Contains(code))
        {
            // Beklenen fresh-stack durumu: topic henüz yok. Warning (Error değil) — anomali değil, normal startup.
            OrderEventsLog.TopicUnavailable(logger, code.ToString());
        }
        else
        {
            // Diğer non-fatal (geçici broker/ağ kesintisi): consumer hayatta kalsın ama Error seviyesinde GÖRÜNÜR
            // olsun → sessiz sonsuz retry yok (gerçek bir sorun loglardan fark edilir).
            OrderEventsLog.ConsumeError(logger, code.ToString(), exception);
        }

        // ★ Backoff: hot-loop'u ve log spam'i önler (her ms değil, döngü başına TEK log). stoppingToken'a duyarlı →
        // shutdown'da hemen iptal; sonraki while koşulu döngüden çıkar (Close).
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
            var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

            switch (await ProcessAsync(context, typeName, result.Message.Value, cancellationToken))
            {
                case ProcessOutcome.UnknownType:
                    OrderEventsLog.UnknownType(logger, typeName);
                    break;
                case ProcessOutcome.AlreadyProcessed:
                    OrderEventsLog.DuplicateSkipped(logger, typeName); // event-id dedup → projection/revenue'ya dokunma.
                    break;
                default: // Applied → projection + InboxMessage ATOMİK tek SaveChanges (Faz 3 inbox precedent'i).
                    await context.SaveChangesAsync(cancellationToken); // 1) DB COMMIT
                    break;
            }

            consumer.Commit(result); // 2) ★ DB'den SONRA offset commit; duplicate/unknown'da da ilerlet (skip → takılma yok).
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

    // Tip header'a göre dispatch + ★ event-id dedup + projection apply. Daha önce işlendiyse → AlreadyProcessed
    // (projection/inbox'a DOKUNMA). İlk kez → apply + InboxMessage stamp (consumer tek SaveChanges'te ATOMİK commit
    // eder). Bilinmeyen tip → UnknownType; deserialize fail → JsonException (poison, caller commit'ler).
    private async Task<ProcessOutcome> ProcessAsync(
        AnalyticsDbContext context, string typeName, string json, CancellationToken cancellationToken)
    {
        if (typeName == CreatedType)
        {
            var integrationEvent = Deserialize<OrderCreatedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, integrationEvent.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            if (await context.OrderProjections.FindAsync([integrationEvent.OrderId], cancellationToken) is null)
            {
                context.OrderProjections.Add(OrderProjection.Create(
                    integrationEvent.OrderId, integrationEvent.CustomerId, integrationEvent.Amount,
                    integrationEvent.Currency, integrationEvent.OccurredOnUtc, integrationEvent.OccurredOnUtc));
            }

            Stamp(context, integrationEvent.Id, typeName);
            OrderEventsLog.Applied(logger, typeName, integrationEvent.OrderId);
            return ProcessOutcome.Applied;
        }

        if (typeName == ConfirmedType)
        {
            var integrationEvent = Deserialize<OrderConfirmedIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, integrationEvent.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await UpdateExistingAsync(
                context, integrationEvent.OrderId, projection => projection.MarkConfirmed(integrationEvent.OccurredOnUtc),
                typeName, cancellationToken);
            Stamp(context, integrationEvent.Id, typeName);
            return ProcessOutcome.Applied;
        }

        if (typeName == PaidType)
        {
            var integrationEvent = Deserialize<OrderPaidIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, integrationEvent.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await ApplyPaidAsync(context, integrationEvent, typeName, cancellationToken);
            Stamp(context, integrationEvent.Id, typeName);
            return ProcessOutcome.Applied;
        }

        if (typeName == CancelledType)
        {
            var integrationEvent = Deserialize<OrderCancelledIntegrationEvent>(json);
            if (await IsDuplicateAsync(context, integrationEvent.Id, typeName, cancellationToken))
            {
                return ProcessOutcome.AlreadyProcessed;
            }

            await UpdateExistingAsync(
                context, integrationEvent.OrderId, projection => projection.MarkCancelled(integrationEvent.OccurredOnUtc),
                typeName, cancellationToken);
            Stamp(context, integrationEvent.Id, typeName);
            return ProcessOutcome.Applied;
        }

        return ProcessOutcome.UnknownType;
    }

    // OrderPaid: status'u Paid'e geçir + ★ o günün gelirine siparişin total'ini ekle. Revenue idempotency'si
    // DEDUP'tan gelir (bu metoda yalnız ilk kez ulaşılır) — MarkPaid status-guard'ına BAĞLI DEĞİL.
    private async Task ApplyPaidAsync(
        AnalyticsDbContext context, OrderPaidIntegrationEvent integrationEvent, string typeName, CancellationToken cancellationToken)
    {
        var projection = await context.OrderProjections.FindAsync([integrationEvent.OrderId], cancellationToken);
        if (projection is null)
        {
            OrderEventsLog.ProjectionMissing(logger, typeName, integrationEvent.OrderId);
            return; // anomali: total bilinmiyor → gelir eklenmez (InboxMessage yine stamp'lenir → idempotent).
        }

        projection.MarkPaid(integrationEvent.OccurredOnUtc);

        // amount = OrderProjection.Total (Created/Confirmed set etti; ordering → Paid'de satır var).
        var day = DateOnly.FromDateTime(integrationEvent.OccurredOnUtc);
        var revenue = await context.DailyRevenueProjections.FindAsync([day], cancellationToken);
        if (revenue is null)
        {
            revenue = DailyRevenueProjection.Create(day);
            context.DailyRevenueProjections.Add(revenue);
        }

        revenue.AddPaidOrder(projection.Total);
        OrderEventsLog.Applied(logger, typeName, integrationEvent.OrderId);
    }

    private static async Task<bool> IsDuplicateAsync(
        AnalyticsDbContext context, Guid eventId, string typeName, CancellationToken cancellationToken) =>
        await context.InboxMessages.AsNoTracking()
            .AnyAsync(message => message.MessageId == eventId && message.MessageType == typeName, cancellationToken);

    private static void Stamp(AnalyticsDbContext context, Guid eventId, string typeName) =>
        context.InboxMessages.Add(InboxMessage.Create(eventId, typeName));

    private enum ProcessOutcome
    {
        Applied,
        AlreadyProcessed,
        UnknownType,
    }

    private async Task UpdateExistingAsync(
        AnalyticsDbContext context, Guid orderId, Action<OrderProjection> apply, string typeName, CancellationToken cancellationToken)
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
