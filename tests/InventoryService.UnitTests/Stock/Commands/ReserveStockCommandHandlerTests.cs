using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Stock.Commands.ReserveStock;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Domain.Stock.Events;

namespace OrderHub.InventoryService.UnitTests.Stock.Commands;

public sealed class ReserveStockCommandHandlerTests
{
    private readonly Mock<IInventoryRepository> _repository = new();
    private readonly ReserveStockCommandHandler _sut;

    public ReserveStockCommandHandlerTests()
    {
        _sut = new ReserveStockCommandHandler(
            _repository.Object,
            NullLogger<ReserveStockCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_SufficientStock_ReturnsSuccessAndCreatesPendingReservation()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, 10);
        _repository
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockItem);

        var result = await _sut.Handle(
            new ReserveStockCommand(orderId, productId, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stockItem.AvailableQuantity.Should().Be(7);
        stockItem.Reservations.Should().ContainSingle(r =>
            r.OrderId == orderId && r.Status == ReservationStatus.Pending && r.Quantity == 3);
        stockItem.DomainEvents.Should().ContainSingle(e => e is StockReserved);
    }

    [Fact]
    public async Task Handle_InsufficientStock_ReturnsSuccessButRaisesReservationFailedEvent()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, 2);
        _repository
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockItem);

        var result = await _sut.Handle(
            new ReserveStockCommand(orderId, productId, 5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("insufficient stock is a normal business outcome, not a handler error");
        stockItem.AvailableQuantity.Should().Be(2, "available quantity must be unchanged when stock is insufficient");
        stockItem.Reservations.Should().BeEmpty("no reservation is created when stock is insufficient");
        stockItem.DomainEvents.Should().ContainSingle(e => e is StockReservationFailed);
    }

    [Fact]
    public async Task Handle_StockItemNotFound_ReturnsFailure()
    {
        var command = new ReserveStockCommand(Guid.NewGuid(), Guid.NewGuid(), 1);
        _repository
            .Setup(r => r.GetByProductIdAsync(command.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockItem?)null);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Inventory.StockItemNotFound");
    }
}
