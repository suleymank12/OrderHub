using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderHub.Gateway.Identity;

namespace OrderHub.Gateway.Extensions;

/// <summary>
/// Gateway JWT bearer doğrulaması (downstream servislerin <c>AddJwtAuthentication</c>'ıyla birebir aynı
/// <see cref="TokenValidationParameters"/>). Geçersiz/eksik token'lı korumalı route'lar YARP'ın <c>default</c>
/// authorization policy'siyle (RequireAuthenticatedUser) gateway'de <b>401</b> alır → downstream'e hiç gitmez.
/// </summary>
internal static class GatewayAuthenticationExtensions
{
    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Secret eksik/kısa → startup'ta fail-fast (ValidateOnStart), K3/downstream ile tutarlı.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // "sub" raw kalsın (rate-limit partition'ı bunu okur).
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(); // default policy = RequireAuthenticatedUser → YARP route "default" bunu kullanır.

        return services;
    }
}
