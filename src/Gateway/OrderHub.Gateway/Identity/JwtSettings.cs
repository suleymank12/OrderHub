using System.ComponentModel.DataAnnotations;

namespace OrderHub.Gateway.Identity;

/// <summary>
/// Gateway JWT doğrulama ayarları. Downstream servislerle <b>AYNI</b> Secret/Issuer/Audience — OrderService'in
/// ürettiği token hem gateway'de (merkezi, erken 401) hem downstream'de (defense-in-depth) geçerlidir. Secret asla
/// appsettings'te tutulmaz (K3) → env var <c>JwtSettings__Secret</c>. Gateway token <b>üretmez</b> (yalnız doğrular).
/// </summary>
internal sealed class JwtSettings
{
    /// <summary>Configuration bölüm adı.</summary>
    public const string SectionName = "JwtSettings";

    // HMAC-SHA256 için anahtar ≥ 256 bit → en az 32 karakter (downstream JwtSettings ile aynı kısıt).
    private const int MinimumSecretLength = 32;

    /// <summary>Doğrulama simetrik anahtarı (downstream ile aynı).</summary>
    [Required]
    [MinLength(MinimumSecretLength)]
    public string Secret { get; init; } = string.Empty;

    /// <summary>Token'ı üreten taraf (OrderService: <c>OrderHub.OrderService</c>).</summary>
    [Required]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Token'ın hedef kitlesi (<c>OrderHub.Clients</c>).</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;
}
