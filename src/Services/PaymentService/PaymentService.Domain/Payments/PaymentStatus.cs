namespace OrderHub.PaymentService.Domain.Payments;

/// <summary>
/// Ödeme yaşam döngüsü durumları. <see cref="Pending"/> başlangıç; <see cref="Succeeded"/> ve
/// <see cref="Failed"/> terminaldir (yeniden geçiş yok → idempotent işleme).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Oluşturuldu, sağlayıcı sonucu bekleniyor (varsayılan).</summary>
    Pending = 0,

    /// <summary>Ödeme başarılı (terminal).</summary>
    Succeeded = 1,

    /// <summary>Ödeme başarısız (terminal).</summary>
    Failed = 2
}
