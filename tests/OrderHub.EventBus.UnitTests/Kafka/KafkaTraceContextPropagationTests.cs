using System.Diagnostics;
using Confluent.Kafka;
using OrderHub.EventBus.Kafka;

namespace OrderHub.EventBus.UnitTests.Kafka;

/// <summary>
/// Faz 6 6c-2 — Kafka W3C traceparent propagation helper unit testleri (broker'sız, deterministik). Inject→Extract
/// round-trip trace-id'yi korur; header yoksa yeni root döner (yarım-trace değil, bilinçli yeni trace).
/// </summary>
public sealed class KafkaTraceContextPropagationTests
{
    [Fact]
    public void InjectThenExtract_RoundTrips_PreservesTraceAndSpanId()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var context = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        var headers = new Headers();

        KafkaTraceContextPropagation.Inject(context, headers);
        var extracted = KafkaTraceContextPropagation.Extract(headers);

        headers.Should().Contain(h => h.Key == "traceparent", "W3C traceparent header yazılmalı");
        extracted.TraceId.Should().Be(traceId, "Kafka hop trace-id'yi korumalı (inject↔extract)");
        extracted.SpanId.Should().Be(spanId);
    }

    [Fact]
    public void Inject_CalledTwice_WritesSingleTraceparentHeader_Idempotent()
    {
        var context = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var headers = new Headers();

        KafkaTraceContextPropagation.Inject(context, headers);
        KafkaTraceContextPropagation.Inject(context, headers); // retry/duplicate publish

        headers.Count(h => h.Key == "traceparent").Should().Be(1, "çift inject çift header YAZMAMALI (idempotent)");
    }

    [Fact]
    public void Extract_NoTraceparentHeader_ReturnsDefaultContext()
    {
        var headers = new Headers { { "message-type", "x"u8.ToArray() } };

        var extracted = KafkaTraceContextPropagation.Extract(headers);

        extracted.Should().Be(default(ActivityContext),
            "traceparent yoksa boş context (yeni root) — propagation yokluğunda çökme değil, bilinçli yeni trace");
    }
}
