using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.InventoryService.Application.Abstractions.Persistence;
using OrderHub.InventoryService.Application.Stock.Commands.ConfirmStockReservation;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Domain.Stock.Events;

namespace OrderHub.InventoryService.UnitTests.Stock.Commands;

public sealed class ConfirmStockReservationCommandHandlerTests
{
    private readonly Mock<IInventoryRepository> _repository = new();
    private readonly ConfirmStockReservationCommandHandler _sut;

    public ConfirmStockReservationCommandHandlerTests()
    {
        _sut = new ConfirmStockReservationCommandHandler(
            _repository.Object,
            NullLogger<ConfirmStockReservationCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ExistingPendingReservation_ConfirmsReservationAndReturnsSuccess()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, 10);
        stockItem.Reserve(orderId, 3, DateTime.UtcNow.AddMinutes(15));
        stockItem.ClearDomainEvents(); // isolate: only assert events raised by Confirm
        _repository
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockItem);

        var result = await _sut.Handle(
            new ConfirmStockReservationCommand(orderId, productId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stockItem.Reservations.Should().ContainSingle(r =>
            r.OrderId == orderId && r.Status == ReservationStatus.Confirmed);
        // 5d-3: olay ProductId taşır (outbox map → integration event → saga per-ürün confirm sayacı).
        stockItem.DomainEvents.OfType<StockReservationConfirmed>().Should()
            .ContainSingle(e => e.ProductId == productId && e.OrderId == orderId);
    }

    [Fact]
    public async Task Handle_StockItemNotFound_ReturnsFailure()
    {
        var command = new ConfirmStockReservationCommand(Guid.NewGuid(), Guid.NewGuid());
        _repository
            .Setup(r => r.GetByProductIdAsync(command.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockItem?)null);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Inventory.StockItemNotFound");
    }
}
