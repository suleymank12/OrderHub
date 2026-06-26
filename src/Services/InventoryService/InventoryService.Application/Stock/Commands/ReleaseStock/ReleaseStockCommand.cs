using OrderHub.InventoryService.Application.Abstractions.Messaging;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReleaseStock;

/// <summary>
/// Bir siparişe ait Pending rezervasyonu serbest bırakır (compensation). Ödeme başarısız olduğunda veya
/// sipariş iptal edildiğinde saga bu command'i tetikler. Ayrılan stok <c>StockItem.ReleaseReservation</c>
/// aracılığıyla <c>AvailableQuantity</c>'ye iade edilir; <c>StockReleased</c> domain olayı yükseltilir.
/// </summary>
/// <param name="OrderId">Rezervasyonu serbest bırakılacak sipariş kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği (stok kalemi arama anahtarı).</param>
public sealed record ReleaseStockCommand(Guid OrderId, Guid ProductId) : ICommand;
