namespace OrderHub.OrderService.Application.ValueObjects.Dtos;

/// <summary>
/// <c>Address</c> value object'inin dış kontrat temsili. Hem inbound (<c>CreateOrderRequest</c>) hem
/// outbound (<c>OrderDto</c>) tarafta kullanılır; tüm alanlar zorunludur (doğrulama validator'da).
/// </summary>
/// <param name="Street">Sokak/cadde satırı.</param>
/// <param name="City">Şehir.</param>
/// <param name="PostalCode">Posta kodu.</param>
/// <param name="Country">Ülke.</param>
public sealed record AddressDto(string Street, string City, string PostalCode, string Country);
