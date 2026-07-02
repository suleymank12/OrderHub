using OrderHub.InventoryService.Application.Abstractions.Messaging;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReserveStock;

/// <summary>
/// Bir sipariş için belirtilen ürünün stok kaleminden <paramref name="Quantity"/> kadar rezervasyon yapar.
/// Stok yeterliyse <c>StockReserved</c> domain olayı yükseltilir; yetersizse <c>StockReservationFailed</c>
/// yükseltilir — her iki sonuçta da handler <c>Result.Success()</c> döner (yetersiz stok NORMAL iş sonucudur,
/// TransactionBehavior commits, outbox sinyal taşır, saga compensation tetiklenir; ADR-0007 Karar 2).
/// </summary>
/// <param name="OrderId">Rezervasyonu talep eden sipariş kimliği.</param>
/// <param name="ProductId">Stok ayrılacak ürün kimliği.</param>
/// <param name="Quantity">Ayrılmak istenen miktar (pozitif tamsayı).</param>
public sealed record ReserveStockCommand(Guid OrderId, Guid ProductId, int Quantity) : ICommand;
