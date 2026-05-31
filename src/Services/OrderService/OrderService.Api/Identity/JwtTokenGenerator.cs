using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OrderHub.OrderService.Api.Identity;

/// <summary>
/// <see cref="ITokenGenerator"/>'ın HMAC-SHA256 implementasyonu. <see cref="JwtSettings"/>'teki secret ile
/// imzalar; <c>sub</c> claim'ine kullanıcı kimliğini koyar (<see cref="CurrentUserService"/> bunu okur).
/// Stateless → singleton.
/// </summary>
internal sealed class JwtTokenGenerator(IOptions<JwtSettings> options) : ITokenGenerator
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Guid userId)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return (tokenString, expiresAtUtc);
    }
}
