using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Application.Orders.Commands.FailOrderPayment;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.FailOrderPayment;

public sealed class FailOrderPaymentCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly FailOrderPaymentCommandHandler _sut;

    public FailOrderPaymentCommandHandlerTests()
    {
        _sut = new FailOrderPaymentCommandHandler(
            _repository.Object,
            NullLogger<FailOrderPaymentCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ConfirmedOrder_CancelsWithPaymentFailedReasonAndReturnsTrue()
    {
        var order = OrderFactory.ConfirmedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new FailOrderPaymentCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("iptal uygulandı");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(OrderCancellationReasons.PaymentFailed);
        order.DomainEvents.OfType<OrderCancelled>().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AlreadyCancelledOrder_IsIdempotentNoOpReturnsFalse()
    {
        var order = OrderFactory.CancelledOrder();
        var existingReason = order.CancellationReason;
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        Result<bool>? result = null;
        var act = async () => result = await _sut.Handle(new FailOrderPaymentCommand(order.Id), CancellationToken.None);

        // Idempotent: exception YOK (explicit assert), transition YOK (false), gerekçe değişmez.
        await act.Should().NotThrowAsync();
        result!.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("zaten Cancelled → no-op");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(existingReason, "ikinci iptal gerekçeyi ezmemeli");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNotFoundFailure()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new FailOrderPaymentCommand(orderId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
