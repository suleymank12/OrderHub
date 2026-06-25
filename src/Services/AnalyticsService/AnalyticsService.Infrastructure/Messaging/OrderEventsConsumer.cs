using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Infrastructure.Persistence;
using OrderHub.Contracts.Orders;
using OrderHub.EventBus.Kafka;

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

            if (!await ApplyAsync(context, typeName, result.Message.Value, cancellationToken))
            {
                OrderEventsLog.UnknownType(logger, typeName);
                consumer.Commit(result); // bilinmeyen tip → skip.
                return;
            }

            await context.SaveChangesAsync(cancellationToken); // 1) DB COMMIT
            consumer.Commit(result);                            // 2) ★ SONRA offset commit (at-least-once)
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

    // Tip header'a göre dispatch + projection apply. Bilinmeyen tip → false (skip). Deserialize fail → JsonException (poison).
    private async Task<bool> ApplyAsync(
        AnalyticsDbContext context, string typeName, string json, CancellationToken cancellationToken)
    {
        if (typeName == CreatedType)
        {
            var integrationEvent = Deserialize<OrderCreatedIntegrationEvent>(json);
            if (await context.OrderProjections.FindAsync([integrationEvent.OrderId], cancellationToken) is null)
            {
                context.OrderProjections.Add(OrderProjection.Create(
                    integrationEvent.OrderId, integrationEvent.CustomerId, integrationEvent.Amount,
                    integrationEvent.Currency, integrationEvent.OccurredOnUtc, integrationEvent.OccurredOnUtc));
            }

            OrderEventsLog.Applied(logger, typeName, integrationEvent.OrderId);
            return true;
        }

        if (typeName == ConfirmedType)
        {
            var integrationEvent = Deserialize<OrderConfirmedIntegrationEvent>(json);
            await UpdateExistingAsync(
                context, integrationEvent.OrderId, projection => projection.MarkConfirmed(integrationEvent.OccurredOnUtc),
                typeName, cancellationToken);
            return true;
        }

        if (typeName == PaidType)
        {
            var integrationEvent = Deserialize<OrderPaidIntegrationEvent>(json);
            await UpdateExistingAsync(
                context, integrationEvent.OrderId, projection => projection.MarkPaid(integrationEvent.OccurredOnUtc),
                typeName, cancellationToken);
            return true;
        }

        if (typeName == CancelledType)
        {
            var integrationEvent = Deserialize<OrderCancelledIntegrationEvent>(json);
            await UpdateExistingAsync(
                context, integrationEvent.OrderId, projection => projection.MarkCancelled(integrationEvent.OccurredOnUtc),
                typeName, cancellationToken);
            return true;
        }

        return false;
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
