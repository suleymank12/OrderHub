using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Stock.Commands.ReleaseStock;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Domain.Stock.Events;

namespace OrderHub.InventoryService.UnitTests.Stock.Commands;

public sealed class ReleaseStockCommandHandlerTests
{
    private readonly Mock<IInventoryRepository> _repository = new();
    private readonly ReleaseStockCommandHandler _sut;

    public ReleaseStockCommandHandlerTests()
    {
        _sut = new ReleaseStockCommandHandler(
            _repository.Object,
            NullLogger<ReleaseStockCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ExistingPendingReservation_ReleasesReservationAndRestoresQuantity()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, 10);
        stockItem.Reserve(orderId, 3, DateTime.UtcNow.AddMinutes(15));
        stockItem.ClearDomainEvents(); // isolate: only assert events raised by Release
        _repository
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockItem);

        var result = await _sut.Handle(
            new ReleaseStockCommand(orderId, productId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stockItem.AvailableQuantity.Should().Be(10, "released quantity (3) must be returned to the available pool");
        stockItem.Reservations.Should().ContainSingle(r =>
            r.OrderId == orderId && r.Status == ReservationStatus.Released);
        stockItem.DomainEvents.Should().ContainSingle(e => e is StockReleased);
    }

    [Fact]
    public async Task Handle_StockItemNotFound_ReturnsFailure()
    {
        var command = new ReleaseStockCommand(Guid.NewGuid(), Guid.NewGuid());
        _repository
            .Setup(r => r.GetByProductIdAsync(command.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockItem?)null);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Inventory.StockItemNotFound");
    }
}
