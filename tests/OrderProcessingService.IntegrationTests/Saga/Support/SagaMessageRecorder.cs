namespace OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

/// <summary>
/// Test-double consumer'ların (<see cref="SagaCommandRecordingConsumer"/>) gerçek broker üzerinden aldığı
/// saga command'lerini kaydeden thread-safe kayıt defteri. Consumer'lar farklı thread'lerde koşabilir →
/// <c>lock</c> ile korunur. Assertion'lar tip-bazlı sayı/snapshot ve bounded-wait ile yapılır (flaky değil:
/// pozitif sinyal bekle, sonra doğrula).
/// </summary>
internal sealed class SagaMessageRecorder
{
    private readonly object _gate = new();
    private readonly List<object> _messages = [];

    public void Record(object message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    /// <summary>Kaydedilmiş <typeparamref name="T"/> mesajlarının anlık kopyası (kilit altında).</summary>
    public IReadOnlyList<T> Snapshot<T>()
    {
        lock (_gate)
        {
            return _messages.OfType<T>().ToList();
        }
    }

    public int Count<T>() => Snapshot<T>().Count;

    /// <summary>
    /// En az <paramref name="count"/> adet <typeparamref name="T"/> kaydedilene kadar bekler (bounded — dışarıdaki
    /// CancellationToken timeout'u sınırlar). Broker asenkron teslimatını deterministik hâle getiren pozitif sinyal.
    /// </summary>
    public async Task<IReadOnlyList<T>> WaitForCountAsync<T>(int count, CancellationToken ct)
    {
        while (true)
        {
            var snapshot = Snapshot<T>();
            if (snapshot.Count >= count)
            {
                return snapshot;
            }

            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
        }
    }
}
