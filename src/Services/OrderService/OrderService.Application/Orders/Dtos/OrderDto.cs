using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.Application.Orders.Dtos;

/// <summary>
/// Bir siparişin outbound (Api'ye giden) tam temsili. <see cref="Status"/> string'tir (enum adı) →
/// API kontrat stabilitesi. Domain entity'si asla doğrudan serialize edilmez; Mapster ile bu tipe
/// map'lenir (outbound-only kural).
/// </summary>
/// <param name="Id">Sipariş kimliği.</param>
/// <param name="CustomerId">Müşteri kimliği.</param>
/// <param name="Status">Durum adı (ör. "Pending", "Confirmed").</param>
/// <param name="Total">Toplam tutar.</param>
/// <param name="ShippingAddress">Teslimat adresi.</param>
/// <param name="CreatedAtUtc">Oluşturulma zamanı (UTC).</param>
/// <param name="ConfirmedAtUtc">Onaylanma zamanı (UTC); onaylanmadıysa null.</param>
/// <param name="CancelledAtUtc">İptal zamanı (UTC); iptal edilmediyse null.</param>
/// <param name="CancellationReason">İptal gerekçesi; iptal edilmediyse null.</param>
/// <param name="Items">Sipariş kalemleri.</param>
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    MoneyDto Total,
    AddressDto ShippingAddress,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items);
