using Moq;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Queries.GetOrderById;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Queries;

public sealed class GetOrderByIdQueryHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly GetOrderByIdQueryHandler _sut;

    public GetOrderByIdQueryHandlerTests() =>
        _sut = new GetOrderByIdQueryHandler(_repository.Object, MapperFactory.Create());

    [Fact]
    public async Task Handle_OrderExists_ReturnsSuccessWithFullyMappedDto()
    {
        var order = Order.Create(
            Guid.NewGuid(),
            AddressFactory.Default(),
            [OrderItemFactory.WithPrice(50m, Currency.TRY, 2)]);
        _repository
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _sut.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Id.Should().Be(order.Id);
        dto.CustomerId.Should().Be(order.CustomerId);
        dto.Status.Should().Be("Pending");
        dto.Total.Amount.Should().Be(100m);
        dto.Total.Currency.Should().Be("TRY");
        dto.ShippingAddress.City.Should().Be("Istanbul");
        dto.Items.Should().ContainSingle();
        dto.Items[0].Quantity.Should().Be(2);
        dto.Items[0].UnitPrice.Amount.Should().Be(50m);
        dto.Items[0].Subtotal.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsFailure()
    {
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OrderNotFound_ErrorCodeIsOrderNotFound()
    {
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Order.NotFound");
    }
}
