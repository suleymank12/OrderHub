using OrderHub.Common.Exceptions;

namespace OrderHub.InventoryService.Domain.Stock.Exceptions;

/// <summary>
/// Bir <see cref="StockItem"/> üzerinde mevcut miktardan fazla rezervasyon denendiğinde fırlatılır
/// ("ayrılan ≤ mevcut" invariant ihlali). Saga tarafında stok-yetersiz compensation'ını tetikler (5c handler
/// bu exception'ı yakalayıp <c>StockReservationFailed</c> yayımlar).
/// </summary>
public sealed class InsufficientStockException : DomainException
{
    /// <summary>İlgili stok kalemi kimliği.</summary>
    public Guid StockItemId { get; }

    /// <summary>İlgili ürün kimliği.</summary>
    public Guid ProductId { get; }

    /// <summary>İstenen (ayrılmak istenen) miktar.</summary>
    public int Requested { get; }

    /// <summary>O an satılabilir (mevcut) miktar.</summary>
    public int Available { get; }

    /// <summary>Stok kalemi, ürün, istenen ve mevcut miktarla exception oluşturur.</summary>
    public InsufficientStockException(Guid stockItemId, Guid productId, int requested, int available)
        : base($"Insufficient stock for product {productId} (item {stockItemId}): requested {requested}, available {available}.")
    {
        StockItemId = stockItemId;
        ProductId = productId;
        Requested = requested;
        Available = available;
    }
}
