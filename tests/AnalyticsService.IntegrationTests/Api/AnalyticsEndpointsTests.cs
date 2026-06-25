using System.Net;
using System.Net.Http.Json;
using OrderHub.AnalyticsService.Application.Orders.Dtos;
using OrderHub.AnalyticsService.Application.Revenue.Dtos;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.AnalyticsService.IntegrationTests.Fixtures;

namespace OrderHub.AnalyticsService.IntegrationTests.Api;

/// <summary>
/// Read-only analytics endpoint'lerinin uçtan uca (HTTP → MediatR → read-repo → gerçek SQL) doğrulaması:
/// 200/404, revenue aralık filtresi + boş aralık, JWT (401) ve validator-pipeline (400). Her test öncesi DB reset.
/// </summary>
[Collection(AnalyticsApiCollection.Name)]
public sealed class AnalyticsEndpointsTests(AnalyticsApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOrderProjectionById_Exists_Returns200WithDto()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        var paidAt = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        await factory.SeedAsync(context =>
        {
            var projection = OrderProjection.Create(orderId, customerId, 250.50m, "TRY", createdAt, createdAt);
            projection.MarkPaid(paidAt);
            context.OrderProjections.Add(projection);
            return Task.CompletedTask;
        });

        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(new Uri($"/api/analytics/orders/{orderId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<OrderProjectionDto>();
        dto.Should().NotBeNull();
        dto!.OrderId.Should().Be(orderId);
        dto.CustomerId.Should().Be(customerId);
        dto.Status.Should().Be("Paid");
        dto.Total.Should().Be(250.50m);
        dto.Currency.Should().Be("TRY");
        dto.PaidAtUtc.Should().Be(paidAt);
    }

    [Fact]
    public async Task GetOrderProjectionById_NotFound_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/analytics/orders/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrderProjectionById_NoToken_Returns401()
    {
        var client = factory.CreateClient(); // token YOK

        var response = await client.GetAsync(new Uri($"/api/analytics/orders/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDailyRevenue_RangeFiltersRows_Returns200WithMatchingDays()
    {
        await factory.SeedAsync(context =>
        {
            context.DailyRevenueProjections.Add(SeedRevenue(new DateOnly(2026, 5, 31), 10m)); // aralık dışı (önce)
            context.DailyRevenueProjections.Add(SeedRevenue(new DateOnly(2026, 6, 1), 100m));  // içeride
            context.DailyRevenueProjections.Add(SeedRevenue(new DateOnly(2026, 6, 2), 200m));  // içeride
            context.DailyRevenueProjections.Add(SeedRevenue(new DateOnly(2026, 6, 3), 300m));  // aralık dışı (sonra)
            return Task.CompletedTask;
        });

        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            new Uri("/api/analytics/revenue/daily?from=2026-06-01&to=2026-06-02", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<DailyRevenueDto>>() ?? [];
        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be(new DateOnly(2026, 6, 1));
        rows[0].TotalRevenue.Should().Be(100m);
        rows[1].Date.Should().Be(new DateOnly(2026, 6, 2));
        rows[1].TotalRevenue.Should().Be(200m);
    }

    [Fact]
    public async Task GetDailyRevenue_EmptyRange_Returns200WithEmptyList()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            new Uri("/api/analytics/revenue/daily?from=2026-01-01&to=2026-01-31", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<DailyRevenueDto>>();
        rows.Should().NotBeNull();
        rows!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyRevenue_FromAfterTo_Returns400()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            new Uri("/api/analytics/revenue/daily?from=2026-06-30&to=2026-06-01", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDailyRevenue_NoToken_Returns401()
    {
        var client = factory.CreateClient(); // token YOK

        var response = await client.GetAsync(
            new Uri("/api/analytics/revenue/daily?from=2026-06-01&to=2026-06-02", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static DailyRevenueProjection SeedRevenue(DateOnly date, decimal amount)
    {
        var revenue = DailyRevenueProjection.Create(date);
        revenue.AddPaidOrder(amount);
        return revenue;
    }
}
