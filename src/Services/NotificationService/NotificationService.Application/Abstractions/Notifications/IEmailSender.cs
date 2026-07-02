namespace OrderHub.NotificationService.Application.Abstractions.Notifications;

/// <summary>
/// E-posta bildirimi türü. Infrastructure <see cref="IEmailSender"/> implementasyonuna hangi şablonun
/// kullanılacağını bildirir.
/// </summary>
public enum NotificationEmailKind
{
    OrderConfirmed,
    CartAbandonmentReminder,
}

/// <summary>
/// E-posta gönderim abstraction'ı (DIP). Infrastructure'daki <c>MockEmailSender</c> implement eder
/// (5f-2 showcase — gerçek SMTP 5f-3+). Application bu abstraction'a bağlanır, SMTP'ye değil.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        NotificationEmailKind kind,
        Guid customerId,
        Guid orderId,
        CancellationToken cancellationToken);
}
