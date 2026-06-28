namespace OrderHub.OrderService.Domain.Orders.Events;

/// <summary>
/// <see cref="OrderCreated"/> domain olayında taşınan kalem özeti. Saga (Faz 5) bu bilgiden stok rezervasyonu
/// (<c>ReserveStock</c>) kurar → yalnızca <see cref="ProductId"/> + <see cref="Quantity"/> taşınır; birim fiyat
/// (<c>Money</c>) rezervasyonla ilgisizdir, sızdırılmaz. Tutar bilgisi olayın <c>Total</c>'ında zaten vardır.
/// </summary>
/// <param name="ProductId">Ürün kimliği.</param>
/// <param name="Quantity">Ayrılacak adet (pozitif — <c>OrderItem</c> invariant'ı garanti eder).</param>
public sealed record OrderCreatedItem(Guid ProductId, int Quantity);
