using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Commands.CancelOrder;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.CancelOrder;

public sealed class CancelOrderCommandHandlerTests
{
    private const string Reason = "stock_unavailable";

    private readonly Mock<IOrderRepository> _repository = new();
    private readonly CancelOrderCommandHandler _sut;

    public CancelOrderCommandHandlerTests()
    {
        _sut = new CancelOrderCommandHandler(
            _repository.Object,
            NullLogger<CancelOrderCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_PendingOrder_CancelsAndReturnsTrue()
    {
        var order = OrderFactory.PendingOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new CancelOrderCommand(order.Id, Reason), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("geçiş uygulandı");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(Reason);
        order.DomainEvents.OfType<OrderCancelled>().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ConfirmedOrder_CancelsAndReturnsTrue()
    {
        var order = OrderFactory.ConfirmedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new CancelOrderCommand(order.Id, Reason), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_AlreadyCancelledOrder_IsIdempotentNoOpReturnsFalse()
    {
        var order = OrderFactory.CancelledOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var eventsBefore = order.DomainEvents.OfType<OrderCancelled>().Count();

        var result = await _sut.Handle(new CancelOrderCommand(order.Id, Reason), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("zaten Cancelled → no-op (redelivery ack)");
        order.DomainEvents.OfType<OrderCancelled>().Should().HaveCount(eventsBefore, "ikinci kez OrderCancelled yükselmemeli");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNotFoundFailure()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new CancelOrderCommand(orderId, Reason), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_PaidOrder_ReturnsConflictFailure_NoThrow()
    {
        // Edge: telafi Paid'e ULAŞMAZ (forward-only); savunmacı → Failure (Conflict), throw YOK, iptal uygulanmaz.
        var order = OrderFactory.PaidOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        Result<bool>? result = null;
        var act = async () => result = await _sut.Handle(new CancelOrderCommand(order.Id, Reason), CancellationToken.None);

        await act.Should().NotThrowAsync();
        result!.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        order.Status.Should().Be(OrderStatus.Paid, "iptal uygulanmadı");
    }
}
