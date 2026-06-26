namespace OrderHub.AnalyticsService.Domain.Orders;

/// <summary>
/// Bir siparişin denormalize <b>read-model</b>'i (CQRS read-side, ADR-0006). Kafka order-stream event'leriyle
/// projeksiyon olarak güncellenir; PK = <see cref="OrderId"/>. Aggregate <b>değildir</b> (türetilmiş read state,
/// invariant'sız). ★ Event-apply (durum geçişleri: Confirm/Pay/Cancel) logic'i 4c-2/4c-3'te eklenir — burada
/// yalnız alanlar (private setter) + ilk-oluşturma factory'si (<see cref="Create"/>, OrderCreated'tan).
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

    /// <summary>Ödeme zamanı (UTC); henüz ödenmemişse null.</summary>
    public DateTime? PaidAtUtc { get; private set; }

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
    // Bu, event-id dedup DEĞİLDİR (o 4c-3; revenue idempotency'si için). Money/wire tipi sızdırılmaz → primitive param.

    /// <summary>OrderConfirmed: yalnız Created'tan Confirmed'e ilerletir (aksi → no-op).</summary>
    public void MarkConfirmed(DateTime occurredAtUtc)
    {
        if (Status == OrderProjectionStatus.Created)
        {
            Status = OrderProjectionStatus.Confirmed;
            LastUpdatedUtc = occurredAtUtc;
        }
    }

    /// <summary>OrderPaid: Created/Confirmed'den Paid'e ilerletir + ödeme zamanını set eder (aksi → no-op).</summary>
    public void MarkPaid(DateTime occurredAtUtc)
    {
        if (Status is OrderProjectionStatus.Created or OrderProjectionStatus.Confirmed)
        {
            Status = OrderProjectionStatus.Paid;
            PaidAtUtc = occurredAtUtc;
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
}
