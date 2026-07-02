using OrderHub.Common.Primitives;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Domain.Orders.Exceptions;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Domain.Orders;

/// <summary>
/// Sipariş aggregate root'u. Kalemler yalnızca oluşturma anında verilir ve sonradan değişmez.
/// Durum geçişleri behavior method'larıyla yapılır; her geçiş ilgili domain olayını yükseltir.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    /// <summary>Müşteri kimliği.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Sipariş kalemleri (salt-okunur).</summary>
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    /// <summary>Mevcut durum.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>Toplam tutar; kalem ara toplamlarının toplamı (EF tarafından doldurulur).</summary>
    public Money Total { get; private set; } = null!;

    /// <summary>Teslimat adresi (EF tarafından doldurulur).</summary>
    public Address ShippingAddress { get; private set; } = null!;

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Onaylanma zamanı (UTC); onaylanmadıysa null.</summary>
    public DateTime? ConfirmedAtUtc { get; private set; }

    /// <summary>Ödenme zamanı (UTC); ödenmediyse null.</summary>
    public DateTime? PaidAtUtc { get; private set; }

    /// <summary>Kargolanma zamanı (UTC); kargolanmadıysa null.</summary>
    public DateTime? ShippedAtUtc { get; private set; }

    /// <summary>İptal zamanı (UTC); iptal edilmediyse null.</summary>
    public DateTime? CancelledAtUtc { get; private set; }

    /// <summary>İptal gerekçesi; iptal edilmediyse null.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Yeni bir sipariş oluşturur (durum: Pending) ve <see cref="OrderCreated"/> yükseltir.
    /// <paramref name="customerId"/> boş → <see cref="ArgumentException"/>;
    /// <paramref name="items"/> boş → <see cref="EmptyOrderException"/>;
    /// kalemler farklı currency içeriyorsa → <see cref="InvalidOperationException"/> (Money.Sum).
    /// </summary>
    public static Order Create(Guid customerId, Address shippingAddress, IEnumerable<OrderItem> items)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer id cannot be empty.", nameof(customerId));
        }

        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            throw new EmptyOrderException();
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            Total = Money.Sum(itemList.Select(item => item.Subtotal))
        };

        order._items.AddRange(itemList);
        order.RaiseDomainEvent(new OrderCreated(
            order.Id,
            customerId,
            order.Total,
            itemList.Select(item => new OrderCreatedItem(item.ProductId, item.Quantity)).ToList()));
        return order;
    }

    /// <summary>
    /// Siparişi onaylar (Pending → Confirmed) ve <see cref="OrderConfirmed"/> yükseltir.
    /// Zaten onaylıysa → <see cref="OrderAlreadyConfirmedException"/>; Pending dışındaysa →
    /// <see cref="InvalidOrderStatusTransitionException"/>.
    /// </summary>
    public void Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            throw new OrderAlreadyConfirmedException(Id);
        }

        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Confirmed);
        }

        Status = OrderStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new OrderConfirmed(Id, CustomerId, Total));
    }

    /// <summary>
    /// Siparişi ödenmiş işaretler (Confirmed → Paid) ve <see cref="OrderPaid"/> yükseltir. <b>Idempotent</b>:
    /// zaten Paid ise no-op (throw etmez — at-least-once mesaj teslimatı + status guard precedent'i). Confirmed
    /// dışındaki geçersiz durumlardan (örn. Pending/Cancelled) → <see cref="InvalidOrderStatusTransitionException"/>.
    /// </summary>
    public void MarkPaid()
    {
        if (Status == OrderStatus.Paid)
        {
            return; // Idempotent no-op: aynı PaymentSucceeded ikinci kez gelirse güvenli.
        }

        if (Status != OrderStatus.Confirmed)
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Paid);
        }

        Status = OrderStatus.Paid;
        PaidAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new OrderPaid(Id));
    }

    /// <summary>
    /// Siparişi kargolanmış işaretler (Paid → Shipped) ve <see cref="OrderShipped"/> yükseltir. <b>Idempotent</b>:
    /// zaten Shipped ise no-op (throw etmez — at-least-once saga-command teslimatı + status guard precedent'i,
    /// <see cref="MarkPaid"/> ile aynı; no-op'ta yeni event yok). Paid dışındaki durumlardan (örn. Confirmed/
    /// Cancelled) → <see cref="InvalidOrderStatusTransitionException"/>.
    /// </summary>
    public void Ship()
    {
        if (Status == OrderStatus.Shipped)
        {
            return; // Idempotent no-op: aynı ShipOrder ikinci kez gelirse güvenli.
        }

        if (Status != OrderStatus.Paid)
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Shipped);
        }

        Status = OrderStatus.Shipped;
        ShippedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new OrderShipped(Id));
    }

    /// <summary>
    /// Siparişi iptal eder (Pending/Confirmed → Cancelled) ve <see cref="OrderCancelled"/> yükseltir.
    /// <b>Idempotent</b>: zaten Cancelled ise no-op (throw etmez, yeni event yok — at-least-once saga-command
    /// teslimatı + <see cref="MarkPaid"/>/<see cref="Ship"/> idempotency precedent'i). <paramref name="reason"/>
    /// boş → <see cref="ArgumentException"/>; Paid/Shipped'ten iptal → <see cref="InvalidOrderStatusTransitionException"/>
    /// (refund/compensation gerektirir, kapsam dışı — saga telafisi bu state'lere ulaşmaz: AwaitingStockConfirmation
    /// forward-only).
    /// </summary>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason cannot be empty.", nameof(reason));
        }

        if (Status == OrderStatus.Cancelled)
        {
            return; // Idempotent no-op: aynı CancelOrder ikinci kez gelirse güvenli (yeni OrderCancelled yükselmez).
        }

        if (Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Cancelled);
        }

        Status = OrderStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        RaiseDomainEvent(new OrderCancelled(Id, CancellationReason));
    }
}
