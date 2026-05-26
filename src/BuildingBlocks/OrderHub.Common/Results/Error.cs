namespace OrderHub.Common.Results;

/// <summary>
/// Başarısız bir <see cref="Result"/>'ün taşıdığı hata bilgisi. Immutable (record):
/// makine-okunur <paramref name="Code"/>, insan-okunur <paramref name="Message"/> ve
/// HTTP map'leme için <paramref name="Type"/>.
/// </summary>
/// <param name="Code">Stabil, makine-okunur hata kodu (ör. "Order.NotFound").</param>
/// <param name="Message">İnsan-okunur açıklama.</param>
/// <param name="Type">Hata kategorisi.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Başarılı (hatasız) durumu temsil eden boş hata.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Doğrulama hatası üretir (→ 400).</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Bulunamadı hatası üretir (→ 404).</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Çakışma hatası üretir (→ 409).</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Genel iş hatası üretir.</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
