using Hangfire;
using Microsoft.Extensions.Options;
using OrderHub.NotificationService.Application.Abstractions.Scheduling;
using OrderHub.NotificationService.Application.Notifications;

namespace OrderHub.NotificationService.Infrastructure.Scheduling;

/// <summary>
/// <see cref="ICartAbandonmentScheduler"/>'ın Hangfire adapter'ı: gecikmeli
/// <see cref="CartAbandonmentReminderJob"/> planlar. Hangfire bağımlılığı bu Infrastructure sınıfında
/// izoledir (Application Hangfire'ı bilmez, DIP). <c>CancellationToken.None</c> yalnızca expression
/// yer tutucusudur; Hangfire çalışma anında gerçek bir iptal token'ı enjekte eder.
/// OrderService <c>HangfireOrderTimeoutScheduler</c> precedent'i. Scoped → scope başına IBackgroundJobClient.
/// </summary>
internal sealed class HangfireCartAbandonmentScheduler(
    IBackgroundJobClient backgroundJobClient,
    IOptions<CartAbandonmentOptions> options) : ICartAbandonmentScheduler
{
    public void ScheduleReminder(Guid orderId) =>
        backgroundJobClient.Schedule<CartAbandonmentReminderJob>(
            job => job.ExecuteAsync(orderId, CancellationToken.None),
            options.Value.ReminderDelay);
}
