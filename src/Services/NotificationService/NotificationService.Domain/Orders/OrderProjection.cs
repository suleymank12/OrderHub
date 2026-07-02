namespace OrderHub.NotificationService.Domain.Orders;

/// <summary>
/// Bir siparişin denormalize <b>read-model</b>'i (CQRS read-side, bildirim kanalı). Kafka order-stream
/// event'leriyle projeksiyon olarak güncellenir; PK = <see cref="OrderId"/>. Aggregate <b>değildir</b>
/// (türetilmiş read state, invariant'sız). ★ <see cref="ReminderSentUtc"/>: 5f-2 sepet-terk hatırlatıcısı
/// için şimdi alanı ekle; behavior (job / email) bu fazda YOK. İleri-only durum geçişleri → out-of-order
/// safe, idempotent (status geriye gidemez).
/// </summary>
public sealed class OrderProjection
{
    private OrderProjection()
    {
    }

    private OrderProjection(
        Guid orderId, Guid customerId, decimal total, string currency, DateTime createdAtUtc, DateTime lastUpdatedUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Status = OrderProjectionStatus.Created;
        Total = total;
        Currency = currency;
        CreatedAtUtc = createdAtUtc;
        LastUpdatedUtc = lastUpdatedUtc;
    }

    /// <summary>Sipariş kimliği (PK; partition key = OrderId ile uçtan uca tutarlı).</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Sipariş sahibinin müşteri kimliği.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Projection'ın güncel durumu.</summary>
    public OrderProjectionStatus Status { get; private set; }

    /// <summary>Sipariş toplam tutarı.</summary>
    public decimal Total { get; private set; }

    /// <summary>Para birimi (ISO 4217).</summary>
    public string Currency { get; private set; } = null!;

    /// <summary>Siparişin oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// 5f-2 sepet-terk hatırlatma e-postasının gönderildiği zaman (UTC); henüz gönderilmemişse null.
    /// Bu fazda (5f-1) yalnız alan tanımlanır; yazılma davranışı (CartAbandonmentReminderJob) 5f-2'de eklenir.
    /// </summary>
    public DateTime? ReminderSentUtc { get; private set; }

    /// <summary>Projection'ın son güncellenme zamanı (UTC).</summary>
    public DateTime LastUpdatedUtc { get; private set; }

    /// <summary>OrderCreated event'inden ilk projection satırını üretir (status = Created).</summary>
    public static OrderProjection Create(
        Guid orderId, Guid customerId, decimal total, string currency, DateTime createdAtUtc, DateTime lastUpdatedUtc)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id cannot be empty.", nameof(orderId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new OrderProjection(orderId, customerId, total, currency, createdAtUtc, lastUpdatedUtc);
    }

    // ★ İleri-only durum geçişleri (domain consistency): geçiş yalnız "önceki" durumlardan yapılır → out-of-order
    // veya tekrar gelen event status'u GERİ GÖTÜREMEZ ve status için doğal olarak idempotenttir (tekrar = no-op).
    // Bu, event-id dedup DEĞİLDİR (o consumer'ın InboxMessage stamp'i). Para/wire tipi sızdırılmaz → primitive param.

    /// <summary>OrderConfirmed: yalnız Created'tan Confirmed'e ilerletir (aksi → no-op).</summary>
    public void MarkConfirmed(DateTime occurredAtUtc)
    {
        if (Status == OrderProjectionStatus.Created)
        {
            Status = OrderProjectionStatus.Confirmed;
            LastUpdatedUtc = occurredAtUtc;
        }
    }

    /// <summary>OrderPaid: Created/Confirmed'den Paid'e ilerletir (aksi → no-op). Status==Paid ödeme sinyali olarak yeterli.</summary>
    public void MarkPaid(DateTime occurredAtUtc)
    {
        if (Status is OrderProjectionStatus.Created or OrderProjectionStatus.Confirmed)
        {
            Status = OrderProjectionStatus.Paid;
            LastUpdatedUtc = occurredAtUtc;
        }
    }

    /// <summary>OrderCancelled: Created/Confirmed'den Cancelled'e geçirir (ödenmiş sipariş iptal edilmez → no-op).</summary>
    public void MarkCancelled(DateTime occurredAtUtc)
    {
        if (Status is OrderProjectionStatus.Created or OrderProjectionStatus.Confirmed)
        {
            Status = OrderProjectionStatus.Cancelled;
            LastUpdatedUtc = occurredAtUtc;
        }
    }

    /// <summary>
    /// Sepet-terk hatırlatma e-postasının gönderildiğini kaydeder (idempotent: zaten gönderildiyse no-op).
    /// <c>CartAbandonmentReminderJob</c> çağırır; <see cref="ReminderSentUtc"/> dolu ise tekrar gönderim engellenir.
    /// </summary>
    public void MarkReminderSent(DateTime sentAtUtc)
    {
        if (ReminderSentUtc is null)
        {
            ReminderSentUtc = sentAtUtc;
            LastUpdatedUtc = sentAtUtc;
        }
    }
}
