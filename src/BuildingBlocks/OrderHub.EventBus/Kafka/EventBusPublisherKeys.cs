namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Routing publisher'ın alt-transport publisher'larını ayırt ettiği keyed-DI anahtarları (ADR-0006 Karar 3).
/// Public: ileri senaryolarda (ör. test, alternatif transport) keyed kaydı override etmek için sözleşmedir
/// (secret değil — yalnız DI ayrımı).
/// </summary>
public static class EventBusPublisherKeys
{
    /// <summary>RabbitMQ (MassTransit) integration event publisher'ı için keyed-DI anahtarı.</summary>
    public const string RabbitMq = "eventbus:rabbitmq";

    /// <summary>Kafka integration event publisher'ı için keyed-DI anahtarı.</summary>
    public const string Kafka = "eventbus:kafka";
}
