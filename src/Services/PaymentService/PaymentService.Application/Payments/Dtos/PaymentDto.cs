namespace OrderHub.PaymentService.Application.Payments.Dtos;

/// <summary>
/// Bir ödemenin outbound (Api'ye giden) temsili. <see cref="Status"/> string'tir (enum adı) → API kontrat
/// stabilitesi. Domain entity'si asla doğrudan serialize edilmez; Mapster ile bu tipe map'lenir (outbound-only).
/// </summary>
/// <param name="Id">Ödeme kimliği.</param>
/// <param name="OrderId">Sipariş kimliği.</param>
/// <param name="Amount">Tutar.</param>
/// <param name="Currency">Para birimi (ISO 4217).</param>
/// <param name="Status">Durum adı (ör. "Pending", "Succeeded", "Failed").</param>
/// <param name="ExternalTransactionId">Sağlayıcı dış işlem kimliği; başarılı değilse null.</param>
/// <param name="CreatedAtUtc">Oluşturulma zamanı (UTC).</param>
public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? ExternalTransactionId,
    DateTime CreatedAtUtc);
