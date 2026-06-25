using MassTransit;
using OrderHub.EventBus;

namespace OrderHub.PaymentService.IntegrationTests.Messaging;

/// <summary>
/// 3d-3 retry/DLQ testine özel integration event (gerçek ProcessPayment akışını bozmamak için izole — bkz.
/// <see cref="FailingTestConsumer"/>). <see cref="IIntegrationEvent"/> implement eder ki production inbox filter
/// (<c>InboxConsumeFilter&lt;&gt;</c>, generic constraint = IIntegrationEvent) bu mesaja da uygulansın → testte
/// inbox etkileşimi de gözlemlenebilsin.
/// </summary>
public sealed record FailingTestIntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; init; }

    public DateTime OccurredOnUtc { get; init; }
}

/// <summary>
/// Test thread'i ile consumer arasında paylaşılan gözlemci (singleton). Yalnız consume denemelerini sayar;
/// terminal fault/DLQ sinyali gerçek <c>_error</c> queue'dan okunur (test). Smoke testindeki sinyal pattern'i.
/// </summary>
public sealed class RetryProbe
{
    private int _attempts;

    /// <summary>Toplam <see cref="FailingTestConsumer.Consume"/> çağrısı sayısı (1 ilk + N retry).</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    public void RecordAttempt() => Interlocked.Increment(ref _attempts);
}

/// <summary>
/// Her <c>Consume</c>'da denemeyi kaydedip <b>transient</b> bir hata fırlatan test consumer'ı → retry zincirini
/// tetikler, tükenince mesaj <c>_error</c> queue'ya düşer. Kasten <c>SaveChanges</c> ÇAĞIRMAZ → inbox filter'ın
/// tracked-Add'i her denemede rollback olur (commit edilmiş inbox satırı yok) → retry temiz tekrarlanır.
/// </summary>
public sealed class FailingTestConsumer(RetryProbe probe) : IConsumer<FailingTestIntegrationEvent>
{
    public Task Consume(ConsumeContext<FailingTestIntegrationEvent> context)
    {
        probe.RecordAttempt();
        throw new InvalidOperationException(
            "Transient failure (test) — retry zincirini tetikler, tükenince _error DLQ'ya düşer.");
    }
}
