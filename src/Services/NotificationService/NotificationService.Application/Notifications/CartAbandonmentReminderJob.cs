using Microsoft.Extensions.Logging;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Application.Abstractions.Persistence;
using OrderHub.NotificationService.Domain.Orders;

namespace OrderHub.NotificationService.Application.Notifications;

/// <summary>
/// Gecikmeli Hangfire job: oluşturulmuş/onaylanmış ancak henüz ödenmemiş siparişlere sepet-terk
/// hatırlatma e-postası gönderir. <b>Idempotent:</b> Paid/Cancelled → skip (sipariş çözüldü);
/// <see cref="OrderProjection.ReminderSentUtc"/> dolu → skip (double-schedule harmless, guard sağlar).
/// Hangfire at-least-once teslimat nedeniyle aynı job birden çok çalışabilir; guard bunu güvenli kılar.
/// Hangfire'a bağlı DEĞİL: saf Application servisi (DIP). Retry → Infrastructure'daki global filtre.
/// </summary>
internal sealed partial class CartAbandonmentReminderJob(
    INotificationOrderRepository repository,
    IEmailSender emailSender,
    ILogger<CartAbandonmentReminderJob> logger)
{
    public async Task ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var projection = await repository.GetByIdAsync(orderId, cancellationToken);

        if (projection is null)
        {
            LogProjectionNotFound(logger, orderId);
            return;
        }

        if (projection.Status is OrderProjectionStatus.Paid or OrderProjectionStatus.Cancelled)
        {
            LogOrderAlreadyResolved(logger, orderId, projection.Status.ToString());
            return;
        }

        if (projection.ReminderSentUtc is not null)
        {
            LogAlreadySent(logger, orderId, projection.ReminderSentUtc.Value);
            return;
        }

        // Created/Confirmed, ödenmemiş, henüz hatırlatılmamış → e-posta gönder + işaretle + kaydet.
        await emailSender.SendAsync(
            NotificationEmailKind.CartAbandonmentReminder, projection.CustomerId, orderId, cancellationToken);
        projection.MarkReminderSent(DateTime.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);

        LogReminderSent(logger, orderId, projection.CustomerId);
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information,
        Message = "Cart abandonment reminder sent for order {OrderId} (customer {CustomerId})")]
    private static partial void LogReminderSent(ILogger logger, Guid orderId, Guid customerId);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug,
        Message = "Cart abandonment reminder skipped: projection not found for order {OrderId}")]
    private static partial void LogProjectionNotFound(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug,
        Message = "Cart abandonment reminder skipped: order {OrderId} already resolved (status {Status})")]
    private static partial void LogOrderAlreadyResolved(ILogger logger, Guid orderId, string status);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Debug,
        Message = "Cart abandonment reminder skipped: already sent at {SentAtUtc} for order {OrderId}")]
    private static partial void LogAlreadySent(ILogger logger, Guid orderId, DateTime sentAtUtc);
}
