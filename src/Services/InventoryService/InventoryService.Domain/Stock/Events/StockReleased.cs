using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş rezervasyonu serbest bırakıldığında (<see cref="StockItem.ReleaseReservation"/>, compensation)
/// yükseltilen domain olayı; ayrılan miktar tekrar satılabilir hâle gelmiştir. 5c'de outbox ile saga'ya köprülenir
/// (PaymentFailed → ReleaseStock telafisi).
/// </summary>
/// <param name="StockItemId">Stok kalemi kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği (integration event saga'ya taşır).</param>
/// <param name="OrderId">İlgili sipariş kimliği.</param>
/// <param name="Quantity">İade edilen miktar.</param>
public sealed record StockReleased(Guid StockItemId, Guid ProductId, Guid OrderId, int Quantity) : DomainEvent;
