using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderHub.OrderService.Api.Identity;

namespace OrderHub.OrderService.Api.Extensions;

/// <summary>JWT bearer authentication kayıt yardımcıları.</summary>
internal static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Secret eksik/kısa → startup'ta fail-fast (ValidateOnStart). Cryptic runtime hatası yerine net.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TokenValidationParameters için ayarları okur; otorite doğrulama ValidateOnStart'tadır.
        var settings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // "sub" raw kalsın (NameIdentifier auto-mapping kapalı).
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30) // varsayılan 5dk çok gevşek → sıkılaştır.
                };
            });

        services.AddAuthorization();

        return services;
    }
}
