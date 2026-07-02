using OrderHub.NotificationService.Application.Abstractions.Notifications;

namespace OrderHub.NotificationService.Infrastructure.Notifications;

/// <summary>
/// <see cref="MockEmailSender"/> tarafından kaydedilen e-posta gönderim kaydı.
/// Integration testlerin gönderilen e-postaları doğrulayabilmesi için gözlemlenebilir (immutable değer nesnesi).
/// </summary>
public sealed record SentEmail(NotificationEmailKind Kind, Guid CustomerId, Guid OrderId);
