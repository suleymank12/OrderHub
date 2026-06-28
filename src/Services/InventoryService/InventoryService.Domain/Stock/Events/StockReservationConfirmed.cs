using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş rezervasyonu onaylandığında (<see cref="StockItem.ConfirmReservation"/>) yükseltilen domain olayı;
/// stok kalıcı düşmüştür. 5d-3'te outbox ile <c>StockReservationConfirmedIntegrationEvent</c>'e (saga'ya)
/// köprülenir — C2 kararı: saga per-ürün confirm sayacı için bu sonucu görmeli (5c'de yalnız in-process'ti).
/// </summary>
/// <param name="StockItemId">Stok kalemi kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği (5d-3: saga'nın per-ürün confirm sayacı için integration event'e taşınır).</param>
/// <param name="OrderId">İlgili sipariş kimliği.</param>
public sealed record StockReservationConfirmed(Guid StockItemId, Guid ProductId, Guid OrderId) : DomainEvent;
