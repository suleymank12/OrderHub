using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.Api;

/// <summary>Development-only token endpoint'inin davranışı + prod'da var olmaması.</summary>
[Collection(ApiCollection.Name)]
public sealed class DevTokenEndpointTests(ApiTestFactory factory)
{
    [Fact]
    public async Task Post_WithCustomerId_Returns200WithJwtCarryingThatSubject()
    {
        var customerId = Guid.NewGuid();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/token", new { customerId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Subject.Should().Be(customerId.ToString());
        jwt.Issuer.Should().Be("OrderHub.OrderService");
        jwt.Audiences.Should().Contain("OrderHub.Clients");
    }

    [Fact]
    public async Task Post_WithoutCustomerId_GeneratesTokenWithNewGuidSubject()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/token", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Guid.TryParse(jwt.Subject, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_InProductionEnvironment_Returns404()
    {
        // Prod'da endpoint pipeline'a hiç eklenmez (IsDevelopment guard) → kod yolu yok, 404.
        using var productionFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment(Environments.Production));
        var client = productionFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/token", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
