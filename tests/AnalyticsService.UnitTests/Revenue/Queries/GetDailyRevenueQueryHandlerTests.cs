using Moq;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;
using OrderHub.AnalyticsService.Domain.Revenue;
using OrderHub.AnalyticsService.UnitTests.TestData;

namespace OrderHub.AnalyticsService.UnitTests.Revenue.Queries;

public sealed class GetDailyRevenueQueryHandlerTests
{
    private readonly Mock<IAnalyticsReadRepository> _repository = new();
    private readonly GetDailyRevenueQueryHandler _sut;

    public GetDailyRevenueQueryHandlerTests() =>
        _sut = new GetDailyRevenueQueryHandler(_repository.Object, MapperFactory.Create());

    [Fact]
    public async Task Handle_RangeHasRows_ReturnsSuccessWithMappedDtosInOrder()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 3);
        var day1 = DailyRevenueProjection.Create(new DateOnly(2026, 6, 1));
        day1.AddPaidOrder(100m);
        day1.AddPaidOrder(50m); // 2 sipariş, 150 gelir, ort 75
        var day2 = DailyRevenueProjection.Create(new DateOnly(2026, 6, 2));
        day2.AddPaidOrder(200m); // 1 sipariş, 200 gelir, ort 200
        _repository
            .Setup(r => r.GetDailyRevenueAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([day1, day2]);

        var result = await _sut.Handle(new GetDailyRevenueQuery(from, to), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Date.Should().Be(new DateOnly(2026, 6, 1));
        result.Value[0].TotalOrders.Should().Be(2);
        result.Value[0].TotalRevenue.Should().Be(150m);
        result.Value[0].AvgOrderValue.Should().Be(75m);
        result.Value[1].Date.Should().Be(new DateOnly(2026, 6, 2));
        result.Value[1].TotalRevenue.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_EmptyRange_ReturnsSuccessWithEmptyList()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        _repository
            .Setup(r => r.GetDailyRevenueAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.Handle(new GetDailyRevenueQuery(from, to), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForwardsRangeBoundsToRepository()
    {
        var from = new DateOnly(2026, 6, 10);
        var to = new DateOnly(2026, 6, 12);
        _repository
            .Setup(r => r.GetDailyRevenueAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.Handle(new GetDailyRevenueQuery(from, to), CancellationToken.None);

        _repository.Verify(
            r => r.GetDailyRevenueAsync(from, to, It.IsAny<CancellationToken>()), Times.Once);
    }
}
