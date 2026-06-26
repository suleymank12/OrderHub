using System.ComponentModel.DataAnnotations;

namespace OrderHub.AnalyticsService.Api.Identity;

/// <summary>
/// JWT doğrulama ayarları. <b>Secret asla appsettings'te tutulmaz</b> (K3) → user-secrets (dev) veya env var
/// <c>JwtSettings__Secret</c> (prod). DataAnnotations + <c>ValidateOnStart</c> ile eksik/kısa secret startup'ta
/// fail-fast eder. AnalyticsService token üretmez (yalnız doğrular; read-only API).
/// </summary>
public sealed class JwtSettings
{
    /// <summary>Configuration bölüm adı.</summary>
    public const string SectionName = "JwtSettings";

    // HMAC-SHA256 için anahtar ≥ 256 bit olmalı → en az 32 karakter zorunlu.
    private const int MinimumSecretLength = 32;

    /// <summary>İmzalama/doğrulama simetrik anahtarı (secret).</summary>
    [Required]
    [MinLength(MinimumSecretLength)]
    public string Secret { get; init; } = string.Empty;

    /// <summary>Token'ı üreten taraf.</summary>
    [Required]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Token'ın hedef kitlesi.</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;
}
