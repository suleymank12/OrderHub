using OrderHub.EventBus;

namespace OrderHub.Contracts.Payments;

/// <summary>
/// PaymentService → OrderService: bir siparişin ödemesi başarısız oldu (ROADMAP §3.4). OrderService bunu
/// tüketip siparişi <c>Cancelled</c>'a geçirir. Producer ve consumer aynı CLR tipini paylaşır.
/// <para>
/// <see cref="Id"/> kaynak <c>PaymentFailed.EventId</c>'sini taşır (uçtan uca dedup, ADR-0002 Karar 4).
/// Yalnızca primitive alanlar — domain tipi sızdırılmaz.
/// </para>
/// </summary>
public sealed record PaymentFailedIntegrationEvent : IIntegrationEvent
{
    /// <summary>Olay kimliği = kaynak <c>PaymentFailed.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Ödemesi başarısız olan siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Başarısızlık gerekçesi (sağlayıcı mesajı; OrderService kanonik iptal gerekçesini kendi üretir).</summary>
    public required string Reason { get; init; }
}
