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
/// Hata → <c>RetryCount++</c> + Error; <c>MaxRetryCount</c>'a ulaşan mesaj artık çekilmez (DLQ/manual —
/// sonsuz retry yok). <see cref="BackgroundService"/> singleton olduğundan scoped bağımlılıklar (DbContext,
/// publisher) her turda <see cref="IServiceScopeFactory"/> ile yeni scope'tan resolve edilir (captive
/// dependency önlenir).
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
        OutboxMessage message, IIntegrationEventPublisher publisher, CancellationToken cancellationToken)
    {
        try
        {
            var integrationEvent = OutboxMessageSerializer.Deserialize(message.Type, message.Payload);
            await publisher.PublishAsync(integrationEvent, cancellationToken);
            message.MarkProcessed(DateTime.UtcNow);
            OutboxLog.Published(logger, message.Id, message.Type);
        }
#pragma warning disable CA1031 // Tek mesajın hatası (serialize/publish) diğerlerini ve batch'i düşürmemeli → mesaj bazında izole + retry.
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
        }
    }
}
