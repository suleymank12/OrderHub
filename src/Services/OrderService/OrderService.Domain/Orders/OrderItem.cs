using OrderHub.Common.Primitives;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Domain.Orders;

/// <summary>
/// Bir siparişe ait kalem (entity). Yalnızca <see cref="Create"/> ile üretilir ve
/// <see cref="Order"/> tarafından sahiplenilir. Kimlik bazlı eşitlik <see cref="Entity{TId}"/>'den.
/// </summary>
public sealed class OrderItem : Entity<Guid>
{
    private OrderItem()
    {
    }

    private OrderItem(Guid id, Guid productId, int quantity, Money unitPrice)
        : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>Ürün kimliği.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Adet (pozitif).</summary>
    public int Quantity { get; private set; }

    /// <summary>Birim fiyat (EF tarafından doldurulur).</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>Kalem ara toplamı (birim fiyat × adet).</summary>
    public Money Subtotal => UnitPrice * Quantity;

    /// <summary>
    /// Yeni bir sipariş kalemi üretir. <paramref name="productId"/> boş → <see cref="ArgumentException"/>;
    /// <paramref name="quantity"/> ≤ 0 → <see cref="ArgumentOutOfRangeException"/>;
    /// <paramref name="unitPrice"/> null → <see cref="ArgumentNullException"/>.
    /// </summary>
    public static OrderItem Create(Guid productId, int quantity, Money unitPrice)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id cannot be empty.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        }

        ArgumentNullException.ThrowIfNull(unitPrice);

        return new OrderItem(Guid.NewGuid(), productId, quantity, unitPrice);
    }
}
