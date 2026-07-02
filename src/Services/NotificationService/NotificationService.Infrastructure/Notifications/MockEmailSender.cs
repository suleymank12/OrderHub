using Microsoft.Extensions.Logging;
using OrderHub.NotificationService.Application.Abstractions.Notifications;

namespace OrderHub.NotificationService.Infrastructure.Notifications;

/// <summary>
/// <see cref="IEmailSender"/>'ın sahte (mock) implementasyonu: gerçek SMTP YOK (showcase, K3).
/// Singleton — thread-safe liste ile gönderilen e-postaları kaydeder; integration testlerde
/// <see cref="Sent"/> üzerinden gözlemlenebilir. Gerçek SMTP entegrasyonu 5f-3+ fazına bırakıldı (YAGNI).
/// </summary>
internal sealed partial class MockEmailSender(ILogger<MockEmailSender> logger) : IEmailSender
{
    private readonly object _lock = new();
    private readonly List<SentEmail> _sent = [];

    /// <summary>
    /// Gönderilen e-postaların anlık görüntüsü. Lock altında kopyalanır → döndürülen liste immutable snapshot'tır.
    /// </summary>
    public IReadOnlyList<SentEmail> Sent
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToList().AsReadOnly();
            }
        }
    }

    public Task SendAsync(
        NotificationEmailKind kind, Guid customerId, Guid orderId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _sent.Add(new SentEmail(kind, customerId, orderId));
        }

        LogEmailSent(logger, kind, orderId, customerId);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Information,
        Message = "Email sent: {Kind} for order {OrderId} (customer {CustomerId})")]
    private static partial void LogEmailSent(
        ILogger logger, NotificationEmailKind kind, Guid orderId, Guid customerId);
}
