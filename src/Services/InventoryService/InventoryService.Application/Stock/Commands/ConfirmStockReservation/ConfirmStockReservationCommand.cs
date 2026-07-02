using OrderHub.InventoryService.Application.Abstractions.Messaging;

namespace OrderHub.InventoryService.Application.Stock.Commands.ConfirmStockReservation;

/// <summary>
/// Bir siparişe ait Pending rezervasyonu Confirmed'a geçirir. Ödeme onaylandığında saga bu command'i tetikler.
/// Stok kalıcı düşürülmüş durumda zaten (<c>Reserve</c>'de yapılmıştı); bu command yalnızca durum geçişini
/// tamamlar ve <c>StockReservationConfirmed</c> domain olayını yükseltir (outbox aracılığıyla saga'ya sinyal).
/// </summary>
/// <param name="OrderId">Rezervasyonu onaylanacak sipariş kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği (stok kalemi arama anahtarı).</param>
public sealed record ConfirmStockReservationCommand(Guid OrderId, Guid ProductId) : ICommand;
