using OrderHub.Common.Primitives;
using OrderHub.PaymentService.Domain.Payments.Events;
using OrderHub.PaymentService.Domain.Payments.Exceptions;

namespace OrderHub.PaymentService.Domain.Payments;

/// <summary>
/// Ödeme aggregate root'u. Bir sipariş için tek bir ödeme girişimini temsil eder; durum geçişleri
/// behavior method'larıyla yapılır ve her sonuç ilgili domain olayını yükseltir. <c>Money</c> value
/// object'i <b>kullanılmaz</b> — PaymentService OrderService domain'ine bağlanmaz; tutar primitive
/// (<see cref="Amount"/> + <see cref="Currency"/>) taşınır (Contracts kararıyla tutarlı). Hiçbir setter public değil.
/// </summary>
public sealed class Payment : AggregateRoot<Guid>
{
    private Payment()
    {
    }

    /// <summary>Ödemenin ait olduğu sipariş kimliği.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Çekilecek tutar.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Para birimi (ISO 4217 kodu, ör. "TRY").</summary>
    public string Currency { get; private set; } = null!;

    /// <summary>Mevcut durum.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>Sağlayıcının dış işlem kimliği; başarılı değilse null.</summary>
    public string? ExternalTransactionId { get; private set; }

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Yeni bir ödeme oluşturur (durum: Pending). <paramref name="orderId"/> boş → <see cref="ArgumentException"/>;
    /// <paramref name="amount"/> negatif → <see cref="ArgumentOutOfRangeException"/>; <paramref name="currency"/>
    /// boş → <see cref="ArgumentException"/>.
    /// </summary>
    public static Payment Create(Guid orderId, decimal amount, string currency)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id cannot be empty.", nameof(orderId));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Payment amount cannot be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Ödemeyi başarılı işaretler (Pending → Succeeded) ve <see cref="PaymentSucceeded"/> yükseltir.
    /// Pending dışındaysa → <see cref="InvalidPaymentStatusTransitionException"/> (idempotency koruması).
    /// </summary>
    public void MarkSucceeded(string externalTransactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalTransactionId);

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidPaymentStatusTransitionException(Status, PaymentStatus.Succeeded);
        }

        Status = PaymentStatus.Succeeded;
        ExternalTransactionId = externalTransactionId;
        RaiseDomainEvent(new PaymentSucceeded(Id, OrderId, externalTransactionId));
    }

    /// <summary>
    /// Ödemeyi başarısız işaretler (Pending → Failed) ve <see cref="PaymentFailed"/> yükseltir.
    /// Pending dışındaysa → <see cref="InvalidPaymentStatusTransitionException"/>.
    /// </summary>
    public void MarkFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidPaymentStatusTransitionException(Status, PaymentStatus.Failed);
        }

        Status = PaymentStatus.Failed;
        RaiseDomainEvent(new PaymentFailed(Id, OrderId, reason));
    }
}
