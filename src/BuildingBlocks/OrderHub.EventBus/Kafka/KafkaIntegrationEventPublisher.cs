using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace OrderHub.EventBus.Kafka;

/// <summary>
/// <see cref="IIntegrationEventPublisher"/>'ın Kafka implementasyonu (ADR-0006). Yalnız <see cref="IKafkaEvent"/>
/// yayımlar: event'i <b>runtime (somut) tiple</b> JSON'a serialize edip (System.Text.Json — §3 temiz, schema
/// registry yok) <see cref="IKafkaEvent.Topic"/>'e, key = <see cref="IKafkaEvent.PartitionKey"/> ile produce eder.
/// Routing publisher zaten yalnız <see cref="IKafkaEvent"/> yönlendirir; guard defansiftir. <see cref="IProducer{TKey,TValue}"/>
/// singleton (thread-safe, idempotent producer) → bu publisher scoped kaydedilse de tek producer'ı paylaşır.
/// </summary>
internal sealed class KafkaIntegrationEventPublisher(IProducer<string, string> producer) : IIntegrationEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent is not IKafkaEvent kafkaEvent)
        {
            throw new InvalidOperationException(
                $"{nameof(KafkaIntegrationEventPublisher)} requires an {nameof(IKafkaEvent)} " +
                $"(got '{integrationEvent.GetType().Name}').");
        }

        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions);
        var message = new Message<string, string>
        {
            Key = kafkaEvent.PartitionKey,
            Value = payload,
            // Tip header'da (value JSON şema taşımaz, schema registry yok) → consumer dispatch anahtarı.
            Headers = new Headers
            {
                { KafkaMessageHeaders.MessageType, Encoding.UTF8.GetBytes(integrationEvent.GetType().FullName!) },
            },
        };

        await producer.ProduceAsync(kafkaEvent.Topic, message, cancellationToken);
    }
}
