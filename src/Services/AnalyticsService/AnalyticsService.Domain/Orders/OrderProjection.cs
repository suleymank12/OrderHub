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
}
