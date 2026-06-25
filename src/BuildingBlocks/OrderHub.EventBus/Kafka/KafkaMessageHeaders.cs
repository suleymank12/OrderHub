namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Kafka mesaj header anahtarları. Value JSON bir şema/tip taşımaz (schema registry yok, ADR-0006 Karar 5);
/// bu yüzden integration event'in CLR tip adı header'da taşınır → consumer (AnalyticsService) hangi event
/// tipini aldığını bundan anlar ve doğru projection'a dispatch eder.
/// </summary>
public static class KafkaMessageHeaders
{
    /// <summary>Integration event CLR tip adı (<see cref="System.Type.FullName"/>) — consumer dispatch anahtarı.</summary>
    public const string MessageType = "message-type";
}
