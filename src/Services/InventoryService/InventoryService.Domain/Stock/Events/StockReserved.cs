using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş için stok ayrıldığında (<see cref="StockItem.Reserve"/>) yükseltilen domain olayı. Faz 5 5c'de
/// outbox üzerinden <c>StockReservedIntegrationEvent</c>'e (saga'ya) köprülenir. Bu DOMAIN olayıdır
/// (integration event Contracts'ta, 5c).
/// </summary>
/// <param name="StockItemId">Rezervasyonun yapıldığı stok kalemi kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği.</param>
/// <param name="OrderId">Rezervasyonu tetikleyen sipariş kimliği (saga correlation'ı).</param>
/// <param name="Quantity">Ayrılan miktar.</param>
public sealed record StockReserved(Guid StockItemId, Guid ProductId, Guid OrderId, int Quantity) : DomainEvent;
