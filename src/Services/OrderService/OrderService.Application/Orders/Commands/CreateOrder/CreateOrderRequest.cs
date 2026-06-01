using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Sipariş oluşturmak için Api'ye gelen inbound DTO. <b>CustomerId taşımaz</b> — müşteri kimliği
/// kimlik doğrulamasından (<c>ICurrentUserService</c>) türetilir, asla body'den okunmaz (IDOR/
/// mass-assignment koruması, K3). Sunucu tarafı/türetilmiş alan (Id, Subtotal, Status) içermez.
/// </summary>
/// <param name="ShippingAddress">Teslimat adresi.</param>
/// <param name="Items">Sipariş kalemleri.</param>
public sealed record CreateOrderRequest(
    AddressDto ShippingAddress,
    IReadOnlyList<CreateOrderItemRequest> Items);

/// <summary>
/// Inbound sipariş kalemi. <c>OrderItemDto</c>'dan ayrıdır (mass-assignment koruması): client yalnızca
/// ürün, adet ve birim fiyat gönderir. Fiyatın client'tan gelmesi Faz 1.3 kısıtıdır — catalog/pricing
/// servisi yok; fiyat Faz 5'te sunucu tarafına taşınacaktır (gizli TODO değil, dokümante sınır).
/// </summary>
/// <param name="ProductId">Ürün kimliği.</param>
/// <param name="Quantity">Adet.</param>
/// <param name="UnitPrice">Birim fiyat.</param>
public sealed record CreateOrderItemRequest(
    Guid ProductId,
    int Quantity,
    MoneyDto UnitPrice);
