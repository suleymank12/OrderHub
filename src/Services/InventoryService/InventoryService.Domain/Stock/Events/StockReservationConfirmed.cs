using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş rezervasyonu onaylandığında (<see cref="StockItem.ConfirmReservation"/>) yükseltilen domain olayı;
/// stok kalıcı düşmüştür. 5c'de outbox ile <c>ConfirmStockReservation</c> sonucu saga'ya köprülenir.
/// </summary>
/// <param name="StockItemId">Stok kalemi kimliği.</param>
/// <param name="OrderId">İlgili sipariş kimliği.</param>
public sealed record StockReservationConfirmed(Guid StockItemId, Guid OrderId) : DomainEvent;
