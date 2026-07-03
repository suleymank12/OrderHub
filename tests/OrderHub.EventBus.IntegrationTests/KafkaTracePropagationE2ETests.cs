using System.Diagnostics;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderHub.EventBus.IntegrationTests.Fixtures;
using OrderHub.EventBus.Kafka;

namespace OrderHub.EventBus.IntegrationTests;

/// <summary>
/// Faz 6 6c-2 — ★ Kafka hop trace propagation e2e (ADR-0006 / Karar D). GERÇEK Kafka (Testcontainers) + in-memory OTel
/// exporter. Producer (publisher) bir producer span açıp W3C traceparent inject eder → mesaj gerçek Kafka'dan geçer →
/// consumer extract edip consume span'ini o trace'e parent'lar. ★ SPESİFİK assertion: consumer span'i producer ile
/// AYNI TraceId taşır (propagation kırık olsaydı consumer YENİ root başlatırdı → yarım-trace → test FAIL). Deterministik
/// (span'ler exporter'da toplanır, bounded consume + ForceFlush, sonra assert — timing eşitliğine bağlı değil).
/// </summary>
[Collection(KafkaCollection.Name)]
public sealed class KafkaTracePropagationE2ETests(KafkaContainerFixture kafka)
{
    [Fact]
    public async Task ProduceThenConsume_OverRealKafka_ConsumerSpanJoinsProducerTrace()
    {
        var spans = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(KafkaDiagnostics.ActivitySourceName) // producer + consumer span'leri bu source'ta
            .AddInMemoryExporter(spans)
            .Build();

        var topic = $"trace-e2e-{Guid.NewGuid():N}";

        // --- Producer: publisher span (Producer) + W3C traceparent inject → gerçek Kafka'ya publish ---
        using (var producer = new ProducerBuilder<string, string>(
                   KafkaEventBusServiceCollectionExtensions.BuildProducerConfig(kafka.BootstrapServers)).Build())
        {
            var publisher = new KafkaIntegrationEventPublisher(producer);
            await publisher.PublishAsync(new TraceE2EKafkaEvent(topic), CancellationToken.None);
            producer.Flush(TimeSpan.FromSeconds(10));
        }

        // --- Consumer: gerçek Kafka'dan tüket → extract + consume span (gerçek OrderEventsConsumer deseni) ---
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"trace-e2e-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var result = ConsumeWithin(consumer, TimeSpan.FromSeconds(30));
        result.Should().NotBeNull("mesaj gerçek Kafka'dan tüketilmeli");

        var parentContext = KafkaTraceContextPropagation.Extract(result!.Message.Headers);
        using (KafkaDiagnostics.ActivitySource.StartActivity($"{topic} consume", ActivityKind.Consumer, parentContext))
        {
            // gerçek consumer'da işleme burada; test için span'in açılıp kapanması yeterli.
        }

        consumer.Close();
        tracerProvider.ForceFlush();

        // --- ★ Assert: Kafka hop trace'i korudu (yarım-trace DEĞİL) ---
        var producerSpan = spans.Should().ContainSingle(s => s.Kind == ActivityKind.Producer).Subject;
        var consumerSpan = spans.Should().ContainSingle(s => s.Kind == ActivityKind.Consumer).Subject;

        consumerSpan.TraceId.Should().Be(producerSpan.TraceId,
            "★ Kafka hop W3C traceparent taşımalı → consumer span producer ile AYNI trace; propagation kırık olsaydı farklı root (yarım-trace)");
        consumerSpan.ParentSpanId.Should().Be(producerSpan.SpanId,
            "consumer span doğrudan producer span'inin child'ı olmalı (uçtan uca zincir)");
    }

    private static ConsumeResult<string, string>? ConsumeWithin(IConsumer<string, string> consumer, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result?.Message is not null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>Minimal <see cref="IKafkaEvent"/> — yalnız publish/propagation testi için (payload içeriği önemsiz).</summary>
internal sealed record TraceE2EKafkaEvent(string Topic) : IKafkaEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    public string PartitionKey => "trace-e2e-key";
}
