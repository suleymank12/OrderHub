using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.MarkOrderPaid;

public sealed class MarkOrderPaidCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly MarkOrderPaidCommandHandler _sut;

    public MarkOrderPaidCommandHandlerTests()
    {
        _sut = new MarkOrderPaidCommandHandler(
            _repository.Object,
            NullLogger<MarkOrderPaidCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ConfirmedOrder_MarksPaidAndReturnsTrue()
    {
        var order = OrderFactory.ConfirmedOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _sut.Handle(new MarkOrderPaidCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("geçiş uygulandı");
        order.Status.Should().Be(OrderStatus.Paid);
        order.DomainEvents.OfType<OrderPaid>().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AlreadyPaidOrder_IsIdempotentNoOpReturnsFalse()
    {
        var order = OrderFactory.PaidOrder();
        _repository.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        Result<bool>? result = null;
        var act = async () => result = await _sut.Handle(new MarkOrderPaidCommand(order.Id), CancellationToken.None);

        // Idempotent: exception YOK (explicit assert), transition YOK (false), status değişmez, yeni event yükselmez.
        await act.Should().NotThrowAsync();
        result!.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("zaten Paid → no-op");
        order.Status.Should().Be(OrderStatus.Paid);
        order.DomainEvents.OfType<OrderPaid>().Should().ContainSingle("ikinci kez OrderPaid yükselmemeli");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsNotFoundFailure()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new MarkOrderPaidCommand(orderId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
