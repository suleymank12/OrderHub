namespace OrderHub.Outbox.Processing;

/// <summary>
/// <see cref="OutboxProcessorService"/> ayarları. Varsayılanlar ROADMAP §3.2 ile uyumlu (polling 2 sn,
/// max 5 retry). Servis composition root'unda <c>AddOutbox(..., o =&gt; ...)</c> ile override edilebilir.
/// </summary>
public sealed class OutboxProcessorOptions
{
    /// <summary>Polling aralığı (default 2 sn).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tek turda çekilecek maksimum mesaj sayısı (FIFO, OccurredOnUtc sırasıyla).</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Bu sayıya ulaşan mesaj artık çekilmez (DLQ/manual müdahale); sonsuz retry yok (default 5).</summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Tek bir publish denemesinin üst süre sınırı (default 10 sn). Broker erişilemezken MassTransit publish'i
    /// belirsiz süre <b>bloke edebilir</b>; bu timeout fail-fast sağlar → publish iptal olur (transient/deferred)
    /// → poll döngüsü asılmaz, mesaj bir sonraki turda yeniden denenir. Shutdown iptali bundan ayrı ele alınır.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
