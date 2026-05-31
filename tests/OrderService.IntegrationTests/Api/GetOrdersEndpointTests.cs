using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderHub.OrderService.Application.Common.Pagination;
using OrderHub.OrderService.Application.Orders.Dtos;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.Api;

/// <summary>GET /api/orders ve /api/orders/{id} uçlarının HTTP davranışları: auth, 200/404, sayfalama.</summary>
[Collection(ApiCollection.Name)]
public sealed class GetOrdersEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetById_ExistingOrder_Returns200WithOrderDto()
    {
        var orderId = await factory.SeedOrderAsync();
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"/api/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<OrderDto>();
        dto!.Id.Should().Be(orderId);
        dto.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetById_NonExistentOrder_Returns404ProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Order.NotFound");
    }

    [Fact]
    public async Task GetById_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_Returns200WithPagedResult()
    {
        await factory.SeedOrderAsync();
        await factory.SeedOrderAsync();
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/orders?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<OrderDto>>();
        paged!.TotalCount.Should().Be(2);
        paged.Items.Should().HaveCount(2);
        paged.Page.Should().Be(1);
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithPageSizeAboveMaximum_Returns400()
    {
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/orders?page=1&pageSize=200");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
