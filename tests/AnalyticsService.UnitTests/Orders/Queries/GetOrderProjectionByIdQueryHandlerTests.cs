using Moq;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Application.Orders.Queries.GetOrderProjectionById;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.UnitTests.TestData;

namespace OrderHub.AnalyticsService.UnitTests.Orders.Queries;

public sealed class GetOrderProjectionByIdQueryHandlerTests
{
    private readonly Mock<IAnalyticsReadRepository> _repository = new();
    private readonly GetOrderProjectionByIdQueryHandler _sut;

    public GetOrderProjectionByIdQueryHandlerTests() =>
        _sut = new GetOrderProjectionByIdQueryHandler(_repository.Object, MapperFactory.Create());

    [Fact]
    public async Task Handle_ProjectionExists_ReturnsSuccessWithFullyMappedDto()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var paidAt = new DateTime(2026, 6, 21, 8, 30, 0, DateTimeKind.Utc);
        var projection = OrderProjection.Create(orderId, customerId, 149.90m, "TRY", createdAt, createdAt);
        projection.MarkPaid(paidAt);
        _repository
            .Setup(r => r.GetOrderProjectionByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        var result = await _sut.Handle(new GetOrderProjectionByIdQuery(orderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OrderId.Should().Be(orderId);
        dto.CustomerId.Should().Be(customerId);
        dto.Status.Should().Be("Paid");
        dto.Total.Should().Be(149.90m);
        dto.Currency.Should().Be("TRY");
        dto.CreatedAtUtc.Should().Be(createdAt);
        dto.PaidAtUtc.Should().Be(paidAt);
    }

    [Fact]
    public async Task Handle_UnpaidProjection_MapsNullPaidAt()
    {
        var orderId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var projection = OrderProjection.Create(orderId, Guid.NewGuid(), 50m, "USD", createdAt, createdAt);
        _repository
            .Setup(r => r.GetOrderProjectionByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        var result = await _sut.Handle(new GetOrderProjectionByIdQuery(orderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Created");
        result.Value.PaidAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProjectionNotFound_ReturnsFailure()
    {
        _repository
            .Setup(r => r.GetOrderProjectionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProjection?)null);

        var result = await _sut.Handle(new GetOrderProjectionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ProjectionNotFound_ErrorCodeIsOrderProjectionNotFound()
    {
        _repository
            .Setup(r => r.GetOrderProjectionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProjection?)null);

        var result = await _sut.Handle(new GetOrderProjectionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("OrderProjection.NotFound");
    }
}
