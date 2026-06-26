namespace OrderHub.AnalyticsService.Application.Orders.Dtos;

/// <summary>
/// Bir sipariş read-model'inin outbound (Api'ye giden) temsili. <see cref="Status"/> string'tir (enum adı) →
/// API kontrat stabilitesi. Domain entity'si asla doğrudan serialize edilmez; Mapster ile bu tipe map'lenir
/// (outbound-only).
/// </summary>
/// <param name="OrderId">Sipariş kimliği.</param>
/// <param name="CustomerId">Sipariş sahibinin müşteri kimliği.</param>
/// <param name="Status">Durum adı (ör. "Created", "Confirmed", "Paid", "Cancelled").</param>
/// <param name="Total">Sipariş toplam tutarı.</param>
/// <param name="Currency">Para birimi (ISO 4217).</param>
/// <param name="CreatedAtUtc">Siparişin oluşturulma zamanı (UTC).</param>
/// <param name="PaidAtUtc">Ödeme zamanı (UTC); henüz ödenmemişse null.</param>
public sealed record OrderProjectionDto(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc);
