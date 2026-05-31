namespace OrderHub.OrderService.Api.Identity;

/// <summary>
/// Development ortamında test/demo amaçlı JWT üretir (gerçek bir identity provider / login akışı bu
/// projenin kapsamında değildir). Üretim endpoint'i yalnızca Development'ta map edilir.
/// </summary>
public interface ITokenGenerator
{
    /// <summary>Verilen kullanıcı kimliği için imzalı bir JWT ve sona erme zamanını (UTC) üretir.</summary>
    (string Token, DateTime ExpiresAtUtc) GenerateToken(Guid userId);
}
