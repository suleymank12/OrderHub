namespace OrderHub.EventBus.RabbitMq;

/// <summary>
/// Consumer-side MassTransit retry policy ayarları (ROADMAP §3.5: exponential backoff, max 5). Tüm servisler
/// için <b>aynı</b> altyapısal policy → varsayılanlar burada; ortam bunları override edebilir (retry sayısı/
/// interval secret değildir, K3 kapsamı dışı — ama ortama göre tunable olmalı, bu yüzden hardcode yerine config).
/// Policy bus-level ve <b>en dışta</b> uygulanır (bkz. <see cref="EventBusServiceCollectionExtensions"/>):
/// retry → [her denemede] inbox filter → consumer. Transient hata → tüm consume (inbox dahil) atomik rollback
/// → temiz tekrar (ADR-0005 Karar 4). Tükenince mesaj MassTransit convention'ı ile <c>&lt;queue&gt;_error</c>'a düşer.
/// </summary>
public sealed record RabbitMqRetryOptions
{
    /// <summary>Maksimum retry sayısı (ilk denemeden SONRA). Toplam consume = 1 + <see cref="RetryLimit"/>.</summary>
    public int RetryLimit { get; init; } = 5;

    /// <summary>İlk retry'ın minimum bekleme süresi (exponential taban).</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Exponential büyümenin üst sınırı (bir retry bundan uzun beklemez).</summary>
    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Her denemede eklenen artış payı (exponential eğrinin diferansiyeli).</summary>
    public TimeSpan IntervalDelta { get; init; } = TimeSpan.FromSeconds(2);
}
