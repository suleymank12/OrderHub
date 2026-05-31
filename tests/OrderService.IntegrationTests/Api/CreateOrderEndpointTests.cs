using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Api;

/// <summary>POST /api/orders uçunun HTTP davranışları: auth, validation (ProblemDetails) ve IDOR koruması.</summary>
[Collection(ApiCollection.Name)]
public sealed class CreateOrderEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WithValidRequestAndToken_Returns201AndPersists()
    {
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        // Kalıcılaştığını doğrula: oluşturulan kaydı geri oku.
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var fetch = await client.GetAsync($"/api/orders/{id}");
        fetch.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithEmptyItems_Returns400WithValidationErrors()
    {
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = ApiRequests.WithItems([]);

        var response = await client.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty(nameof(CreateOrderCommand.Items), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithExtraCustomerIdInBody_UsesTokenIdentityNotBody()
    {
        // K3 IDOR: body'de farklı bir customerId gönderilse bile sahip token'dan gelmeli.
        var authenticatedUserId = Guid.NewGuid();
        var spoofedCustomerId = Guid.NewGuid();
        var client = factory.CreateAuthenticatedClient(authenticatedUserId);

        var json = $$"""
        {
          "customerId": "{{spoofedCustomerId}}",
          "shippingAddress": { "street": "123 Main St", "city": "Istanbul", "postalCode": "34000", "country": "Türkiye" },
          "items": [ { "productId": "{{Guid.NewGuid()}}", "quantity": 2, "unitPrice": { "amount": 50, "currency": "TRY" } } ]
        }
        """;

        var response = await client.PostAsync("/api/orders", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}");
        var persistedCustomerId = fetched.GetProperty("customerId").GetGuid();

        persistedCustomerId.Should().Be(authenticatedUserId);
        persistedCustomerId.Should().NotBe(spoofedCustomerId);
    }
}
