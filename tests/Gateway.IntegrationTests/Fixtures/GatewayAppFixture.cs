using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OrderHub.Gateway.IntegrationTests.Fixtures;

/// <summary>
/// Gateway'i in-process host'layan test factory + <b>WireMock downstream stub</b>. Config <b>env var ile</b>
/// verilir (ApiTestFactory dersi: minimal hosting <c>AddGatewayAuthentication</c>/YARP config'i eager okur →
/// in-memory override geç kalır): JWT secret (downstream'le aynı), YARP cluster destination'ları → WireMock URL,
/// kısa rate-limit. Test'in ürettiği token gateway secret'iyle doğrulanır (gerçek OrderService'e gerek yok).
/// </summary>
public sealed class GatewayAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string Secret = "gateway-integration-test-secret-key-0123456789"; // ≥32 char (HMAC-SHA256).
    internal const string Issuer = "OrderHub.OrderService";
    internal const string Audience = "OrderHub.Clients";
    internal const int PermitLimit = 20;

    private WireMockServer _downstream = null!;

    public Task InitializeAsync()
    {
        _downstream = WireMockServer.Start();
        _downstream
            .Given(Request.Create().WithPath("/*").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("downstream-ok"));

        var url = _downstream.Url!;
        Environment.SetEnvironmentVariable("JwtSettings__Secret", Secret);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__orderservice__Destinations__primary__Address", url);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__paymentservice__Destinations__primary__Address", url);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__analyticsservice__Destinations__primary__Address", url);
        Environment.SetEnvironmentVariable("RateLimiting__PermitLimit", PermitLimit.ToString(CultureInfo.InvariantCulture));

        _ = Services; // host'u başlat (config env'den okunur).
        return Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(Environments.Development);

    /// <summary>Gateway secret'iyle imzalı, geçerli issuer/audience'lı JWT üretir (verilen <paramref name="subject"/> = sub claim).</summary>
    internal string CreateToken(string subject)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim("sub", subject)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public new async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("JwtSettings__Secret", null);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__orderservice__Destinations__primary__Address", null);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__paymentservice__Destinations__primary__Address", null);
        Environment.SetEnvironmentVariable("ReverseProxy__Clusters__analyticsservice__Destinations__primary__Address", null);
        Environment.SetEnvironmentVariable("RateLimiting__PermitLimit", null);
        _downstream.Stop();
        await base.DisposeAsync();
    }
}
