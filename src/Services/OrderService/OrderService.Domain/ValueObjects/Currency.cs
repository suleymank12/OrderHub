namespace OrderHub.OrderService.Domain.ValueObjects;

/// <summary>
/// Desteklenen para birimleri. enum tercih edildi (tip güvenliği, geçersiz currency
/// imkânsız); yeni birim eklemek bilinçli bir kod değişikliğidir.
/// </summary>
public enum Currency
{
    /// <summary>Türk Lirası (varsayılan; Money daima currency'i explicit alır).</summary>
    TRY = 0,

    /// <summary>ABD Doları.</summary>
    USD = 1,

    /// <summary>Euro.</summary>
    EUR = 2
}
