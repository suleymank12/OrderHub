using System.ComponentModel.DataAnnotations;

namespace OrderHub.Gateway.Resilience;

/// <summary>
/// Gateway→downstream HTTP hop'una uygulanan Polly resilience ayarları (config-driven, <c>Resilience</c> bölümü).
/// 6b-1: per-attempt timeout + circuit-breaker (RETRY YOK — 6b-2). Değerler pozitif olmalı (startup'ta fail-fast).
/// </summary>
internal sealed class GatewayResilienceOptions
{
    /// <summary>Configuration bölüm adı.</summary>
    public const string SectionName = "Resilience";

    /// <summary>Toplam pipeline bütçesi (dış timeout). Tek denemede AttemptTimeout baskın; 6b-2 retry ile anlam kazanır.</summary>
    [Range(1, 600)]
    public int TotalTimeoutSeconds { get; init; } = 30;

    /// <summary>Tek downstream çağrısının (attempt) timeout'u — YARP ActivityTimeout'undan küçük (Polly önce tetiklenir).</summary>
    [Range(1, 600)]
    public int AttemptTimeoutSeconds { get; init; } = 10;

    /// <summary>Per-cluster circuit-breaker ayarları.</summary>
    public CircuitBreakerOptions CircuitBreaker { get; init; } = new();
}

/// <summary>
/// Per-cluster circuit-breaker eşikleri. Polly v8: örnekleme penceresinde <see cref="MinimumThroughput"/> kadar
/// çağrı olup <see cref="FailureRatio"/> aşılınca circuit OPEN → <see cref="BreakDurationSeconds"/> boyunca fast-fail,
/// sonra half-open (tek probe). Her downstream ayrı state (izolasyon).
/// </summary>
internal sealed class CircuitBreakerOptions
{
    /// <summary>OPEN eşiği: penceredeki başarısızlık oranı (0-1). Default 0.5 = %50.</summary>
    [Range(0.01, 1.0)]
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>OPEN değerlendirmesi için penceredeki minimum çağrı sayısı (Polly ≥ 2).</summary>
    [Range(2, int.MaxValue)]
    public int MinimumThroughput { get; init; } = 10;

    /// <summary>Başarısızlık oranının hesaplandığı kayan pencere (sn).</summary>
    [Range(1, 3600)]
    public int SamplingDurationSeconds { get; init; } = 30;

    /// <summary>Circuit OPEN kalma süresi (sn); sonra half-open probe.</summary>
    [Range(1, 3600)]
    public int BreakDurationSeconds { get; init; } = 15;
}
