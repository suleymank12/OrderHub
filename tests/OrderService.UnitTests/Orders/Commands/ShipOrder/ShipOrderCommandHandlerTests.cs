using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Commands.ShipOrder;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.ShipOrder;

public sealed class ShipOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly ShipOrderCommandHandler _sut;

    public ShipOrderCommandHandlerTests()
    {
        _sut = new ShipOrderCommandHandler(
            _repository.Object,
            NullLogger<ShipOrderCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_PaidOrder_ShipsAndReturnsTrue()
    {
        var order = OrderFactory.PaidOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new ShipOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("geçiş uygulandı");
        order.Status.Should().Be(OrderStatus.Shipped);
        order.DomainEvents.OfType<OrderShipped>().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AlreadyShippedOrder_IsIdempotentNoOpReturnsFalse()
    {
        var order = OrderFactory.ShippedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        Result<bool>? result = null;
        var act = async () => result = await _sut.Handle(new ShipOrderCommand(order.Id), CancellationToken.None);

        // Idempotent: exception YOK, transition YOK (false), status değişmez, yeni event yükselmez.
        await act.Should().NotThrowAsync();
        result!.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("zaten Shipped → no-op");
        order.Status.Should().Be(OrderStatus.Shipped);
        order.DomainEvents.OfType<OrderShipped>().Should().ContainSingle("ikinci kez OrderShipped yükselmemeli");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNotFoundFailure()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new ShipOrderCommand(orderId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ConfirmedOrder_IsEdgeNoOpReturnsFalse()
    {
        // Paid olmayan edge (Confirmed): aggregate Ship'i çağrılmaz → throw YOK, no-op (false).
        var order = OrderFactory.ConfirmedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new ShipOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("Paid değil → no-op");
        order.Status.Should().Be(OrderStatus.Confirmed);
    }
}
