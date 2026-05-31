using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Yeni sipariş oluşturma command'i. Başarıda oluşturulan siparişin Id'sini döner. <b>CustomerId
/// yoktur</b> — handler bunu <c>ICurrentUserService</c>'ten alır (IDOR koruması, K3). Item tipi inbound
/// <see cref="CreateOrderItemRequest"/> ile aynıdır (Api katmanı request → command map'lerken aktarır).
/// </summary>
/// <param name="ShippingAddress">Teslimat adresi.</param>
/// <param name="Items">Sipariş kalemleri.</param>
public sealed record CreateOrderCommand(
    AddressDto ShippingAddress,
    IReadOnlyList<CreateOrderItemRequest> Items)
    : ICommand<Guid>;
