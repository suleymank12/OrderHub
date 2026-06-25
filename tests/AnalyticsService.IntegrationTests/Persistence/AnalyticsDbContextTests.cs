using Microsoft.EntityFrameworkCore;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.AnalyticsService.IntegrationTests.Fixtures;

namespace OrderHub.AnalyticsService.IntegrationTests.Persistence;

/// <summary>
/// Faz 4 Adım 4c-1 — AnalyticsService iskeletinin gerçek SQL'e bağlandığının kanıtı: <c>OrderHub_Analytics</c>
/// migration'ı uygulanır (OrderProjections + DailyRevenueProjections tabloları), projection entity'leri
/// round-trip eder (insert → read), UTC converter Kind'ı korur. (Kafka consumer/apply logic 4c-2/4c-3.)
/// </summary>
[Collection(AnalyticsDatabaseCollection.Name)]
public sealed class AnalyticsDbContextTests(AnalyticsSqlServerContainerFixture fixture)
{
    [Fact]
    public async Task OrderProjection_RoundTrips_WithCreatedStatus_AndUtcKindPreserved()
    {
        var orderId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;
        try
        {
            await using (var context = fixture.CreateContext())
            {
                context.OrderProjections.Add(
                    OrderProjection.Create(orderId, Guid.NewGuid(), 150.50m, "TRY", createdAtUtc, createdAtUtc));
                await context.SaveChangesAsync();
            }

            await using var verify = fixture.CreateContext();
            var projection = await verify.OrderProjections.AsNoTracking().SingleAsync(p => p.OrderId == orderId);

            projection.Status.Should().Be(OrderProjectionStatus.Created);
            projection.Total.Should().Be(150.50m);
            projection.Currency.Should().Be("TRY");
            projection.PaidAtUtc.Should().BeNull();
            projection.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc, "UtcDateTimeConverter Kind'ı korur");
        }
        finally
        {
            await using var context = fixture.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM OrderProjections WHERE OrderId = {orderId}");
        }
    }

    [Fact]
    public async Task DailyRevenueProjection_RoundTrips_WithZeroedAggregate()
    {
        var date = new DateOnly(2026, 6, 25);
        try
        {
            await using (var context = fixture.CreateContext())
            {
                context.DailyRevenueProjections.Add(DailyRevenueProjection.Create(date));
                await context.SaveChangesAsync();
            }

            await using var verify = fixture.CreateContext();
            var revenue = await verify.DailyRevenueProjections.AsNoTracking().SingleAsync(r => r.Date == date);

            revenue.TotalOrders.Should().Be(0);
            revenue.TotalRevenue.Should().Be(0m);
            revenue.AvgOrderValue.Should().Be(0m);
        }
        finally
        {
            await using var context = fixture.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM DailyRevenueProjections WHERE [Date] = {date}");
        }
    }
}
