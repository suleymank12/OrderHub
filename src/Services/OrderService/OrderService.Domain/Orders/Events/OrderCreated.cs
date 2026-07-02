using OrderHub.Common.Primitives;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>
/// Yeni bir sipariş oluşturulduğunda yükseltilen domain olayı. <see cref="Items"/> Faz 5'te eklendi (additive):
/// saga stok rezervasyonunu (<c>ReserveStock</c>) bu kalemlerden kurar. Mevcut Kafka köprüsü
/// (<c>OrderCreatedIntegrationEvent</c>, Analytics) kalemleri kullanmaz → davranışı değişmez.
/// </summary>
/// <param name="OrderId">Siparişin kimliği.</param>
/// <param name="CustomerId">Müşteri kimliği.</param>
/// <param name="Total">Sipariş toplam tutarı.</param>
/// <param name="Items">Sipariş kalemleri (ProductId + Quantity); saga rezervasyonu için.</param>
public sealed record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    Money Total,
    IReadOnlyList<OrderCreatedItem> Items) : DomainEvent;
