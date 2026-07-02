using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Commands.ConfirmOrder;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.ConfirmOrder;

public sealed class ConfirmOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly ConfirmOrderCommandHandler _sut;

    public ConfirmOrderCommandHandlerTests()
    {
        _sut = new ConfirmOrderCommandHandler(
            _repository.Object,
            NullLogger<ConfirmOrderCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_PendingOrder_ConfirmsAndReturnsTrue()
    {
        var order = OrderFactory.PendingOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new ConfirmOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("geçiş uygulandı");
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<OrderConfirmed>().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AlreadyConfirmedOrder_IsIdempotentNoOpReturnsFalse()
    {
        var order = OrderFactory.ConfirmedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var eventsBefore = order.DomainEvents.OfType<OrderConfirmed>().Count();

        Result<bool>? result = null;
        var act = async () => result = await _sut.Handle(new ConfirmOrderCommand(order.Id), CancellationToken.None);

        // Idempotent: handler guard'ı aggregate'in OrderAlreadyConfirmedException'ını önler → throw YOK, no-op (false).
        await act.Should().NotThrowAsync();
        result!.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("zaten Confirmed → no-op");
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<OrderConfirmed>().Should().HaveCount(eventsBefore, "ikinci kez OrderConfirmed yükselmemeli");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNotFoundFailure()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new ConfirmOrderCommand(orderId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
