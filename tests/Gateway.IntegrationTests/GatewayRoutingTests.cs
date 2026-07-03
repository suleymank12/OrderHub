using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OrderHub.Gateway.IntegrationTests.Fixtures;

namespace OrderHub.Gateway.IntegrationTests;

/// <summary>
/// Faz 6 6a-1 — gateway proxy core: JWT merkezi doğrulama (erken 401), YARP routing (forward), dev-token
/// anonymous route, per-client rate-limit (429 + Retry-After). Downstream WireMock stub ile izole/deterministik.
/// </summary>
[Collection(GatewayAppCollection.Name)]
public sealed class GatewayRoutingTests(GatewayAppFixture app)
{
    [Fact]
    public async Task ProtectedRoute_WithoutToken_ReturnsUnauthorized_NotForwarded()
    {
        var client = app.CreateClient();

        var response = await client.GetAsync("/api/orders/123");

        // ★ Gateway merkezi JWT: token yok → "default" policy → 401 (downstream'e HİÇ gitmez).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_WithValidToken_ForwardsToDownstream()
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", app.CreateToken(Guid.NewGuid().ToString()));

        var response = await client.GetAsync("/api/orders/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("downstream-ok", "geçerli token → YARP downstream stub'a forward etmeli");
    }

    [Fact]
    public async Task DevTokenRoute_Anonymous_ForwardsWithoutAuthentication()
    {
        var client = app.CreateClient();

        // ★ dev-token route AllowAnonymous → token'sız 401 DEĞİL, forward edilir (token üretme public).
        var response = await client.PostAsync("/api/dev/token", new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RateLimit_ExceededForSingleClient_Returns429WithRetryAfter()
    {
        var client = app.CreateClient();
        // ★ Benzersiz sub → izole partition (diğer testleri etkilemez; deterministik PermitLimit+1'de aşım).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", app.CreateToken($"ratelimit-{Guid.NewGuid()}"));

        HttpResponseMessage? throttled = null;
        for (var i = 0; i <= GatewayAppFixture.PermitLimit; i++)
        {
            var response = await client.GetAsync("/api/orders/x");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }
        }

        throttled.Should().NotBeNull("PermitLimit aşımı → 429");
        throttled!.Headers.Should().ContainKey("Retry-After", "client'a ne kadar bekleyeceği bildirilmeli");
    }
}
