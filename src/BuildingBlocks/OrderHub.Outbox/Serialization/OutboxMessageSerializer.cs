using System.Text.Json;
using OrderHub.EventBus;

namespace OrderHub.Outbox.Serialization;

/// <summary>
/// Integration event ↔ outbox payload dönüşümü (System.Text.Json — §3 temiz, ADR-0004 Karar 3).
/// <see cref="Serialize"/> somut tiple serileştirir (tüm property'ler korunur); tip bilgisi
/// AssemblyQualifiedName olarak saklanır. <see cref="Deserialize"/> aynı tipi geri çözer; tip çözümlenemez
/// veya payload <see cref="IIntegrationEvent"/>'e dönmezse <b>fail-fast</b> (processor bunu yakalar →
/// MarkFailed → retry/DLQ).
/// </summary>
internal static class OutboxMessageSerializer
{
    // Web defaults: camelCase + case-insensitive okuma. Producer'da serialize, yine producer'da deserialize
    // (publish öncesi) → simetrik; consumer tarafı MassTransit'in kendi serializer'ıyla okur (ayrı yol).
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static (string Type, string Payload) Serialize(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var runtimeType = integrationEvent.GetType();
        var type = runtimeType.AssemblyQualifiedName
            ?? throw new InvalidOperationException(
                $"Integration event type '{runtimeType}' has no AssemblyQualifiedName; cannot persist to outbox.");

        var payload = JsonSerializer.Serialize(integrationEvent, runtimeType, SerializerOptions);
        return (type, payload);
    }

    public static IIntegrationEvent Deserialize(string type, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var runtimeType = Type.GetType(type)
            ?? throw new InvalidOperationException($"Cannot resolve outbox message CLR type '{type}'.");

        if (JsonSerializer.Deserialize(payload, runtimeType, SerializerOptions) is not IIntegrationEvent integrationEvent)
        {
            throw new InvalidOperationException(
                $"Outbox payload did not deserialize to {nameof(IIntegrationEvent)} (type '{type}').");
        }

        return integrationEvent;
    }
}
