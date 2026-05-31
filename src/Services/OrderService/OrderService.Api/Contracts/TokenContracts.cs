namespace OrderHub.OrderService.Api.Contracts;

/// <summary>
/// Development-only token endpoint'inin istek gövdesi. <see cref="CustomerId"/> verilmezse yeni bir
/// kimlik üretilir (demo/test kolaylığı). Gerçek bir login/identity provider bu projenin kapsamında değil.
/// </summary>
/// <param name="CustomerId">Token'ın temsil edeceği kullanıcı kimliği (opsiyonel).</param>
public sealed record TokenRequest(Guid? CustomerId);

/// <summary>Development-only token endpoint'inin yanıtı.</summary>
/// <param name="Token">İmzalı JWT.</param>
/// <param name="ExpiresAtUtc">Token'ın sona erme zamanı (UTC).</param>
public sealed record TokenResponse(string Token, DateTime ExpiresAtUtc);
