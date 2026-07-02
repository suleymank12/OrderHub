namespace OrderHub.NotificationService.Application.Abstractions.Scheduling;

/// <summary>
/// Sepet-terk hatırlatma job'unun planlanma abstraction'ı.
/// Infrastructure'daki <c>HangfireCartAbandonmentScheduler</c> Hangfire adapter'ını inject eder (DIP).
/// Api katmanında kayıtlıdır (Hangfire sunucusu ile birlikte); projection-only testlerde YOKTUR → isteğe bağlı resolve.
/// </summary>
public interface ICartAbandonmentScheduler
{
    void ScheduleReminder(Guid orderId);
}
