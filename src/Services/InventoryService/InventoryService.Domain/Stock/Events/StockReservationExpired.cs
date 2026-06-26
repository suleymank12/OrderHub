using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş rezervasyonu 15dk içinde onaylanmadığı için zaman aşımına uğradığında
/// (<see cref="StockItem.ExpireReservation"/>, Hangfire timeout job'u) yükseltilen domain olayı; ayrılan miktar
/// iade edilmiştir. 5c'de outbox ile <c>StockReservationExpired</c> integration event'ine köprülenir → saga
/// (5d) bunu compensation sinyali olarak tüketir (expiry ≠ release, ayrı sinyal — ADR-0007 Karar 2).
/// </summary>
/// <param name="StockItemId">Stok kalemi kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği (integration event saga'ya taşır).</param>
/// <param name="OrderId">İlgili sipariş kimliği.</param>
/// <param name="Quantity">İade edilen miktar.</param>
public sealed record StockReservationExpired(Guid StockItemId, Guid ProductId, Guid OrderId, int Quantity) : DomainEvent;
