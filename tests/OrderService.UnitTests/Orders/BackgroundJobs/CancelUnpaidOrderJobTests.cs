using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.BackgroundJobs;

/// <summary>
/// <see cref="CancelUnpaidOrderJob"/> davranış testleri: idempotency garantileri ve sipariş
/// durumuna göre dal ayrımı. Hangfire bağımlılığı yok; saf Application servisi (C1, ADR-0002).
/// <para>
/// ÖNEMLI: <see cref="OrderStatus.Paid"/> / Shipped durumlarına erişim Faz 3/5'te eklenir.
/// No-op branch'i <see cref="OrderFactory.CancelledOrder()"/> ile test edilir — reflection ile
/// status set EDILMEZ (domain invariant bypass). Cancel → Cancelled geçişi domain'de mevcuttur.
/// </para>
/// </summary>
public sealed class CancelUnpaidOrderJobTests
{
    private readonly Mock<IOrderRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);

    private CancelUnpaidOrderJob BuildSut() =>
        new(
            _repository.Object,
            _unitOfWork.Object,
            NullLogger<CancelUnpaidOrderJob>.Instance);

    // -------------------------------------------------------------------------
    // Senaryo 1: Pending sipariş → iptal edilir, kayıt bir kez yapılır
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderPending_CancelsOrderAndSavesOnce()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = OrderFactory.PendingOrder();

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(orderId, CancellationToken.None);

        // Assert — domain davranışı: sipariş Cancelled olmalı.
        order.Status.Should().Be(OrderStatus.Cancelled);

        // UnitOfWork tam bir kez çağrıldı.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OrderPending_SetsCancellationReasonToPaymentTimeout()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = OrderFactory.PendingOrder();

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(orderId, CancellationToken.None);

        // Assert — gerekçe sabit string ile eşleşmeli (audit edilebilirlik, ADR-0003).
        order.CancellationReason.Should().Be(OrderCancellationReasons.PaymentTimeout);
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Sipariş bulunamaz → no-op (idempotent)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_DoesNotSave()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(orderId, CancellationToken.None);

        // Assert — kayıt yapılmamalı (Strict mock: kurulmamış çağrı exception fırlatır).
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Sipariş Pending değil (Cancelled) → no-op (idempotent)
    //
    // NOT: Paid/Shipped statüsü Faz 3/5'te eklenecek; tek mevcut non-Pending durum Cancelled.
    // Reflection ile status set EDILMEZ — OrderFactory.CancelledOrder() ile domain-valid yol.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderAlreadyCancelled_DoesNotSave()
    {
        // Arrange — Cancelled order: guard "Status != Pending" → no-op.
        var orderId = Guid.NewGuid();
        var cancelledOrder = OrderFactory.CancelledOrder();

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelledOrder);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(orderId, CancellationToken.None);

        // Assert — zaten Cancelled → ek bir SaveChanges olmamalı.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OrderAlreadyCancelled_DoesNotChangeCancellationState()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var cancelledOrder = OrderFactory.CancelledOrder();
        var originalReason = cancelledOrder.CancellationReason;
        var originalCancelledAt = cancelledOrder.CancelledAtUtc;

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelledOrder);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(orderId, CancellationToken.None);

        // Assert — iptal durumu değişmemeli (idempotent no-op).
        cancelledOrder.Status.Should().Be(OrderStatus.Cancelled);
        cancelledOrder.CancellationReason.Should().Be(originalReason);
        cancelledOrder.CancelledAtUtc.Should().Be(originalCancelledAt);
    }
}
