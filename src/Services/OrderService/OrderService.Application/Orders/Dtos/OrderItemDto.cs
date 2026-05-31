using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.Application.Orders.Dtos;

/// <summary>
/// Bir sipariş kaleminin outbound (Api'ye giden) temsili. Yalnızca okuma içindir; türetilmiş
/// <see cref="Subtotal"/> dahil edilir (client tekrar hesaplamasın). Inbound karşılığı ayrı bir tiptir
/// (<c>CreateOrderItemRequest</c>) → mass-assignment koruması (K3).
/// </summary>
/// <param name="Id">Kalem kimliği.</param>
/// <param name="ProductId">Ürün kimliği.</param>
/// <param name="Quantity">Adet.</param>
/// <param name="UnitPrice">Birim fiyat.</param>
/// <param name="Subtotal">Ara toplam (birim fiyat × adet).</param>
public sealed record OrderItemDto(
    Guid Id,
    Guid ProductId,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto Subtotal);
