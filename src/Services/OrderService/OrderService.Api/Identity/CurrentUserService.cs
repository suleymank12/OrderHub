using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using OrderHub.OrderService.Application.Abstractions.Identity;

namespace OrderHub.OrderService.Api.Identity;

/// <summary>
/// <see cref="ICurrentUserService"/>'in HTTP implementasyonu: kimliği <see cref="IHttpContextAccessor"/>
/// üzerinden JWT claim'lerinden okur. <c>MapInboundClaims=false</c> ayarlandığı için claim adı raw
/// <c>sub</c>'tır (ASP.NET'in <see cref="ClaimTypes.NameIdentifier"/>'a otomatik mapping bagajı kapalı).
/// </summary>
internal sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid UserId
    {
        get
        {
            var subject = httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(subject, out var userId) ? userId : Guid.Empty;
        }
    }

    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
