using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Stock.Events;

/// <summary>
/// Bir sipariş için stok ayrılamadığında (yetersiz stok) yükseltilen domain olayı. Stok-yetersiz, saga için
/// <b>NORMAL bir iş sonucudur</b> (exception değil compensation tetikler) — aggregate state'i DEĞİŞMEZ (rezervasyon
/// oluşmaz), bu olay yalnızca sonucu transactional outbox üzerinden saga'ya bildirmek için yükseltilir. 5c'de
/// <c>StockReservationFailed</c> integration event'ine köprülenir (PaymentService PaymentFailed precedent'i:
/// başarısızlık normal akış, domain olayıyla taşınır).
/// </summary>
/// <param name="StockItemId">Stok kalemi kimliği.</param>
/// <param name="ProductId">İlgili ürün kimliği.</param>
/// <param name="OrderId">Rezervasyonu istenen sipariş kimliği.</param>
/// <param name="Reason">Başarısızlık gerekçesi (ör. yetersiz stok detayı).</param>
public sealed record StockReservationFailed(Guid StockItemId, Guid ProductId, Guid OrderId, string Reason) : DomainEvent;
