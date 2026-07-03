using System.Diagnostics;

namespace OrderHub.EventBus.Kafka;

/// <summary>
/// Kafka produce/consume span'leri için ortak <see cref="ActivitySource"/> (Faz 6 6c-2). Producer (bu building
/// block) ve İKİ consumer (Analytics + Notification) AYNI source'u kullanır → tek <c>AddSource</c> ile dinlenir (DRY).
/// ★ <c>ObservabilityExtensions.AddObservability</c> (OrderHub.Observability) bu adı <c>AddSource</c>'a eklemeli — yoksa span'ler
/// TracerProvider tarafından dinlenmez ve exporter'a (Seq) GİTMEZ.
/// </summary>
public static class KafkaDiagnostics
{
    /// <summary>OTel TracerProvider'ın <c>AddSource</c> ile dinlemesi gereken source adı.</summary>
    public const string ActivitySourceName = "OrderHub.EventBus.Kafka";

    /// <summary>Produce/consume span'lerinin üretildiği ortak kaynak (producer + iki consumer paylaşır).</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
