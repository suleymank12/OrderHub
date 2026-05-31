using Moq;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Queries.ListOrders;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Queries;

public sealed class ListOrdersQueryHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly ListOrdersQueryHandler _sut;

    public ListOrdersQueryHandlerTests() =>
        _sut = new ListOrdersQueryHandler(_repository.Object, MapperFactory.Create());

    [Fact]
    public async Task Handle_EmptyRepository_ReturnsSuccessWithEmptyItems()
    {
        _repository
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Order>)[], 0));

        var result = await _sut.Handle(new ListOrdersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MultipleOrders_MapsAllToDto()
    {
        IReadOnlyList<Order> orders = [OrderFactory.PendingOrder(), OrderFactory.PendingOrder(), OrderFactory.PendingOrder()];
        _repository
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((orders, orders.Count));

        var result = await _sut.Handle(new ListOrdersQuery(), CancellationToken.None);

        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Should().OnlyContain(dto => dto.Status == "Pending");
    }

    [Fact]
    public async Task Handle_ComputesPaginationMetadata()
    {
        IReadOnlyList<Order> orders = [OrderFactory.PendingOrder()];
        _repository
            .Setup(r => r.GetPagedAsync(2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((orders, 25));

        var result = await _sut.Handle(new ListOrdersQuery(Page: 2, PageSize: 10), CancellationToken.None);

        var paged = result.Value;
        paged.Page.Should().Be(2);
        paged.PageSize.Should().Be(10);
        paged.TotalCount.Should().Be(25);
        paged.TotalPages.Should().Be(3);
        paged.HasNextPage.Should().BeTrue();
        paged.HasPreviousPage.Should().BeTrue();
    }
}
