using MassTransit;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Smoke testine özel basit mesaj — gerçek ProcessPaymentIntegrationEvent veya domain sözleşmeleri DEĞİL
/// (onlar 3c). Yalnızca "transport çalışıyor mu" round-trip'ini doğrulamak için.
/// </summary>
/// <param name="Id">Korelasyon kimliği (publish edilen = consume edilen).</param>
public sealed record SmokeMessage(Guid Id);

/// <summary>
/// Test boyunca consume sonucunu sinyalleyen yardımcı (manuel TaskCompletionSource). Bus üzerinden gelen
/// mesajı test thread'ine taşır → "gerçekten consume edildi" timeout'lu beklenebilir.
/// </summary>
public sealed class SmokeSignal
{
    private readonly TaskCompletionSource<SmokeMessage> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Consumer mesajı aldığında tamamlanır.</summary>
    public Task<SmokeMessage> Completion => _completion.Task;

    /// <summary>Consumer'dan çağrılır; ilk mesajı yakalar.</summary>
    public void Signal(SmokeMessage message) => _completion.TrySetResult(message);
}

/// <summary>Gerçek RabbitMQ üzerinden gelen <see cref="SmokeMessage"/>'ı tüketip <see cref="SmokeSignal"/>'a iletir.</summary>
public sealed class SmokeConsumer(SmokeSignal signal) : IConsumer<SmokeMessage>
{
    public Task Consume(ConsumeContext<SmokeMessage> context)
    {
        signal.Signal(context.Message);
        return Task.CompletedTask;
    }
}
