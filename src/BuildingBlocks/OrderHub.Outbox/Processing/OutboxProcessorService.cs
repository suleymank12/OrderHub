using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderHub.EventBus;
using OrderHub.Outbox.Serialization;

namespace OrderHub.Outbox.Processing;

/// <summary>
/// İşlenmemiş outbox mesajlarını polling ile okuyup <see cref="IIntegrationEventPublisher"/> ile yayımlayan
/// arka plan servisi (ROADMAP §3.2). At-least-once: önce publish, başarılıysa <c>ProcessedOnUtc</c> set.
/// <para>
/// İki hata sınıfı <b>ayrı</b> ele alınır (ADR-0002 Faz 3 Karar 5): <b>deserialize</b> hatası KALICI/poison
/// (retry düzeltmez) → <c>RetryCount++</c> → <c>MaxRetryCount</c>'ta DLQ (artık çekilmez); <b>publish</b>
/// hatası GEÇİCİ/transient (broker-down) → yalnız "deferred" log, <c>RetryCount</c> ARTMAZ,
/// <c>ProcessedOnUtc</c> null kalır → sonraki poll yeniden dener (broker dönünce publish olur, §3.8).
/// Publish per-mesaj <c>PublishTimeout</c> ile çağrılır (broker bloke ederse fail-fast → döngü asılmaz);
/// shutdown iptali timeout iptalinden ayrılır (shutdown → temiz dur, deferred log basılmaz).
/// </para>
/// <see cref="BackgroundService"/> singleton olduğundan scoped bağımlılıklar (DbContext, publisher) her turda
/// <see cref="IServiceScopeFactory"/> ile yeni scope'tan resolve edilir (captive dependency önlenir).
/// </summary>
internal sealed class OutboxProcessorService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxProcessorOptions> options,
    ILogger<OutboxProcessorService> logger)
    : BackgroundService
{
    private readonly OutboxProcessorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown: batch ortasında iptal (publish/SaveChanges) → temiz dur.
            }
#pragma warning disable CA1031 // Polling loop ASLA ölmemeli: tek batch hatası (DB/broker down) sürekliliği bozamaz → en geniş yakalama bilinçli.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                OutboxLog.BatchFailed(logger, exception);
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // Graceful shutdown.
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null && message.RetryCount < _options.MaxRetryCount)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            await PublishAsync(message, publisher, cancellationToken);
        }

        // Tüm ProcessedOnUtc/RetryCount güncellemeleri tek SaveChanges'te kalıcılaşır.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishAsync(
        OutboxMessage message, IIntegrationEventPublisher publisher, CancellationToken stoppingToken)
    {
        // 1) DESERIALIZE — KALICI/poison: bozuk tip/payload retry ile düzelmez → terminal sayaç (RetryCount++)
        //    → MaxRetryCount'ta DLQ (sorgudan düşer). Bu, gerçek poison'ı sonsuz denemekten korur (ADR-0002 Karar 5).
        IIntegrationEvent integrationEvent;
        try
        {
            integrationEvent = OutboxMessageSerializer.Deserialize(message.Type, message.Payload);
        }
#pragma warning disable CA1031 // Deserialize hatası: tipi/payload'ı bilinmeyen poison → en geniş yakalama bilinçli.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            message.MarkFailed(exception.Message);

            if (message.RetryCount >= _options.MaxRetryCount)
            {
                OutboxLog.DeadLettered(logger, message.Id, message.Type, message.RetryCount, exception);
            }
            else
            {
                OutboxLog.PublishFailed(logger, message.Id, message.Type, message.RetryCount, exception);
            }

            return;
        }

        // 2) PUBLISH — GEÇİCİ/transient: broker bloke ederse poll döngüsü asılmasın diye per-publish timeout
        //    (stoppingToken + PublishTimeout linked-CTS) → fail-fast iptal.
        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        publishCts.CancelAfter(_options.PublishTimeout);

        try
        {
            await publisher.PublishAsync(integrationEvent, publishCts.Token);
            message.MarkProcessed(DateTime.UtcNow);
            OutboxLog.Published(logger, message.Id, message.Type);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // GERÇEK shutdown (timeout değil): mutasyon/loga dokunma, yeniden fırlat → döngü graceful dursun.
            throw;
        }
#pragma warning disable CA1031 // Transient (broker-down VEYA publish-timeout): transport tipine referans YOK → outbox transport-agnostik kalır (ADR-0002 Karar 5).
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // RetryCount ARTMAZ, ProcessedOnUtc null kalır → bir sonraki poll yeniden dener (broker dönünce publish).
            OutboxLog.PublishDeferred(logger, message.Id, message.Type, exception);
        }
    }
}
