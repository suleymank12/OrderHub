using OrderHub.EventBus;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>Stub publisher'ın simüle ettiği broker durumu.</summary>
internal enum OutboxPublisherMode
{
    /// <summary>Broker down → publish bağlantı hatası fırlatır (transient/deferred yolu).</summary>
    BrokerDownThrows,

    /// <summary>Broker down → publish bloke olur (poll döngüsü asılmasın diye PublishTimeout iptal etmeli).</summary>
    BrokerDownBlocks,

    /// <summary>Broker up → publish başarılı (no-op).</summary>
    Healthy,
}

/// <summary>
/// 3d-4a davranış testi için toggle'lanabilir <see cref="IIntegrationEventPublisher"/> stub'ı (gerçek broker YOK).
/// <see cref="Mode"/> runtime'da değiştirilerek broker-down → up geçişi simüle edilir; böylece processor'ın
/// transient (deferred, RetryCount artmaz) ve recovery davranışı deterministik doğrulanır. Exception swallowing
/// değildir — kasıtlı, gözlemlenebilir test double (K5).
/// </summary>
internal sealed class TogglableIntegrationEventPublisher : IIntegrationEventPublisher
{
    private volatile OutboxPublisherMode _mode = OutboxPublisherMode.BrokerDownThrows;

    public OutboxPublisherMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        switch (_mode)
        {
            case OutboxPublisherMode.Healthy:
                return; // Başarı (no-op publish) → processor MarkProcessed eder.

            case OutboxPublisherMode.BrokerDownBlocks:
                // Broker bloke ediyormuş gibi: çağıranın PublishTimeout linked-CTS'i iptal edene dek bekle → fail-fast'ı tetikler.
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;

            default: // BrokerDownThrows
                throw new InvalidOperationException("Broker unavailable (test stub) — transient publish failure.");
        }
    }
}
