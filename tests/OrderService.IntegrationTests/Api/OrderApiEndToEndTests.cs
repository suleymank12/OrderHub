using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderHub.OrderService.Application.Common.Pagination;
using OrderHub.OrderService.Application.Orders.Dtos;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Api;

/// <summary>Tam akış: token → oluştur → gerçek DB'ye yaz → geri oku/listele. Tüm katmanlar uçtan uca.</summary>
[Collection(ApiCollection.Name)]
public sealed class OrderApiEndToEndTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateThenRetrieve_OrderIsPersistedAndFetchable()
    {
        var userId = Guid.NewGuid();
        var client = factory.CreateAuthenticatedClient(userId);

        var create = await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var order = await client.GetFromJsonAsync<OrderDto>($"/api/orders/{id}");

        order!.Id.Should().Be(id);
        order.CustomerId.Should().Be(userId);
        order.Status.Should().Be("Pending");
        order.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateThenList_OrderAppearsInListing()
    {
        var client = factory.CreateAuthenticatedClient(Guid.NewGuid());

        await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());

        var paged = await client.GetFromJsonAsync<PagedResult<OrderDto>>("/api/orders?page=1&pageSize=10");

        paged!.TotalCount.Should().Be(1);
        paged.Items.Should().ContainSingle();
    }
}
