using OrderHub.EventBus;

namespace OrderHub.Contracts.Payments;

/// <summary>
/// OrderService → PaymentService komut-tarzı entegrasyon olayı: "şu sipariş için ödeme işlensin"
/// (ROADMAP §3.4). <c>Order.Confirm()</c> sonrası outbox üzerinden yayımlanır; PaymentService bu tipi
/// tüketir (3.3) → producer ve consumer aynı CLR tipini paylaşır (MassTransit mesaj kimliği eşleşmesi).
/// <para>
/// <see cref="Id"/> kaynak <c>OrderConfirmed.EventId</c>'sini taşır (uçtan uca dedup, ADR-0002 Karar 4).
/// Tutar bilinçli olarak primitive <see cref="Amount"/> + <see cref="Currency"/> (ISO 4217 string) olarak
/// taşınır; <c>Money</c> value object'i sızdırılmaz → PaymentService OrderService domain'ine bağlanmaz.
/// <c>required</c> üyeler tüm alanların construction'da set edilmesini derleme-zamanı zorlar (eksik alan yok).
/// </para>
/// </summary>
public sealed record ProcessPaymentIntegrationEvent : IIntegrationEvent
{
    /// <summary>Olay kimliği = kaynak <c>OrderConfirmed.EventId</c> (dedup anahtarı).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaynak olayın UTC oluşma zamanı.</summary>
    public required DateTime OccurredOnUtc { get; init; }

    /// <summary>Ödemesi işlenecek siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Sipariş sahibinin müşteri kimliği.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Çekilecek tutar (negatif olamaz — kaynak <c>Money</c> bunu garanti eder).</summary>
    public required decimal Amount { get; init; }

    /// <summary>Para birimi (ISO 4217 kodu, ör. "TRY").</summary>
    public required string Currency { get; init; }
}
