using OrderHub.EventBus;

namespace OrderHub.Contracts.Payments;

/// <summary>
/// PaymentService → OrderService: bir siparişin ödemesi başarıyla tamamlandı (ROADMAP §3.4). OrderService
/// bunu tüketip siparişi <c>Paid</c>'e geçirir. Producer ve consumer aynı CLR tipini paylaşır (MassTransit
/// mesaj kimliği eşleşmesi).
/// <para>
/// <see cref="Id"/> kaynak <c>PaymentSucceeded.EventId</c>'sini taşır (uçtan uca dedup, ADR-0002 Karar 4).
/// Yalnızca primitive alanlar — <c>Money</c>/domain tipi sızdırılmaz (servisler birbirinin domain'ine bağlanmaz).
/// </para>
/// </summary>
public sealed record PaymentSucceededIntegrationEvent : IIntegrationEvent
{
    /// <summary>Olay kimliği = kaynak <c>PaymentSucceeded.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Ödemesi tamamlanan siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Sağlayıcının döndürdüğü dış işlem kimliği (audit/izlenebilirlik).</summary>
    public required string ExternalTransactionId { get; init; }
}
