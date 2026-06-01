namespace OrderHub.Common.Results;

/// <summary>
/// Bir <see cref="Error"/>'ün kategorisi. Api katmanı bu kategoriyi HTTP status'a
/// map'ler (ör. <see cref="NotFound"/> → 404, <see cref="Conflict"/> → 409).
/// </summary>
public enum ErrorType
{
    /// <summary>Genel/beklenmeyen iş hatası (varsayılan).</summary>
    Failure = 0,

    /// <summary>Girdi doğrulama hatası (→ 400).</summary>
    Validation = 1,

    /// <summary>Kaynak bulunamadı (→ 404).</summary>
    NotFound = 2,

    /// <summary>Durum/eşzamanlılık çakışması (→ 409).</summary>
    Conflict = 3
}
