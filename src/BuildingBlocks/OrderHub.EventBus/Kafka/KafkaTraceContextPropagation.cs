using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Kafka mesaj header'ları üzerinden <b>W3C traceparent</b> propagation (Faz 6 6c-2, ADR-0006 / Karar D). RabbitMQ
/// hop'ları MassTransit ile OTOMATİK trace context taşır; Kafka custom producer/consumer ise taşımaz → aksi halde
/// Order→Kafka→Analytics/Notification trace'i KOPAR (yarım-trace). Bu helper producer'da inject, consumer'da extract
/// eder → trace tek parça kalır. Ortak (producer + iki consumer paylaşır) → tek doğru inject/extract (DRY).
/// <para>
/// ★ Global <see cref="Propagators.DefaultTextMapPropagator"/> yerine SABİT <see cref="TraceContextPropagator"/>
/// kullanılır: deterministik (SDK init sırasına bağlı değil; SDK kurulmadan da W3C çalışır) ve amacımız tam olarak
/// W3C traceparent (baggage kullanmıyoruz).
/// </para>
/// </summary>
public static class KafkaTraceContextPropagation
{
    private static readonly TextMapPropagator Propagator = new TraceContextPropagator();

    /// <summary>Verilen trace context'i (genelde <see cref="Activity.Current"/>) mesaj header'larına W3C olarak yazar.</summary>
    public static void Inject(ActivityContext context, Headers headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        Propagator.Inject(new PropagationContext(context, Baggage.Current), headers, SetHeader);
    }

    /// <summary>Mesaj header'larından W3C traceparent'ı çözer; yoksa boş <see cref="ActivityContext"/> (yeni root).</summary>
    public static ActivityContext Extract(Headers headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return Propagator.Extract(default, headers, GetHeader).ActivityContext;
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        headers.Remove(key); // idempotent: retry/duplicate publish çift traceparent yazmasın
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static IEnumerable<string> GetHeader(Headers headers, string key) =>
        headers.TryGetLastBytes(key, out var bytes) ? [Encoding.UTF8.GetString(bytes)] : [];
}
