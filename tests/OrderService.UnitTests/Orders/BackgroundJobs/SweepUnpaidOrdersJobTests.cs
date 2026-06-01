using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Application.Orders.Configuration;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.BackgroundJobs;

/// <summary>
/// <see cref="SweepUnpaidOrdersJob"/> davranış testleri: reconciliation backstop.
/// Stale Pending siparişler toplu iptal edilir, tek SaveChanges; boş liste ise SaveChanges hiç olmaz.
/// Saf Application servisi — Hangfire bağımlılığı yok (ADR-0003).
/// </summary>
public sealed class SweepUnpaidOrdersJobTests
{
    private readonly Mock<IOrderRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);

    private SweepUnpaidOrdersJob BuildSut(TimeSpan unpaidTimeout) =>
        new(
            _repository.Object,
            _unitOfWork.Object,
            Options.Create(new OrderTimeoutOptions { UnpaidTimeout = unpaidTimeout }),
            NullLogger<SweepUnpaidOrdersJob>.Instance);

    // -------------------------------------------------------------------------
    // Senaryo 1: Stale Pending sipariş var → hepsi iptal, tek SaveChanges
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PendingOrdersExist_CancelsAllAndSavesOnce()
    {
        // Arrange
        var order1 = OrderFactory.PendingOrder();
        var order2 = OrderFactory.PendingOrder();
        IReadOnlyList<Order> staleOrders = [order1, order2];

        _repository.Setup(r => r.GetPendingOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleOrders);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = BuildSut(TimeSpan.FromMinutes(15));

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — her iki sipariş de Cancelled olmalı.
        order1.Status.Should().Be(OrderStatus.Cancelled);
        order2.Status.Should().Be(OrderStatus.Cancelled);

        // Tek SaveChanges (toplu commit — ADR-0003).
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PendingOrdersExist_SetsCancellationReasonToPaymentTimeout()
    {
        // Arrange
        var order = OrderFactory.PendingOrder();
        IReadOnlyList<Order> staleOrders = [order];

        _repository.Setup(r => r.GetPendingOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleOrders);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = BuildSut(TimeSpan.FromMinutes(15));

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — gerekçe audit-edilebilir sabit olmalı.
        order.CancellationReason.Should().Be(OrderCancellationReasons.PaymentTimeout);
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Stale sipariş yok → SaveChanges çağrılmaz (erken return)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoStaleOrders_DoesNotSave()
    {
        // Arrange
        IReadOnlyList<Order> emptyList = [];

        _repository.Setup(r => r.GetPendingOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyList);

        var sut = BuildSut(TimeSpan.FromMinutes(15));

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — boş liste → erken return → SaveChanges asla çağrılmamalı
        // (Strict mock: kurulmamış çağrı exception fırlatır — ek güvence).
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Cutoff hesaplaması: UtcNow - UnpaidTimeout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CalculatesCutoffAsUtcNowMinusUnpaidTimeout()
    {
        // Arrange — cutoff'a geçilen DateTime'ı yakala.
        var timeout = TimeSpan.FromHours(1);
        DateTime? capturedCutoff = null;
        var before = DateTime.UtcNow;

        IReadOnlyList<Order> emptyList = [];
        _repository
            .Setup(r => r.GetPendingOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((cutoff, _) => capturedCutoff = cutoff)
            .ReturnsAsync(emptyList);

        var sut = BuildSut(timeout);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        var after = DateTime.UtcNow;

        // Assert — cutoff = UtcNow - timeout aralığında olmalı.
        capturedCutoff.Should().NotBeNull();
        capturedCutoff!.Value.Should().BeCloseTo(before - timeout, TimeSpan.FromSeconds(5));
        capturedCutoff.Value.Should().BeOnOrBefore(after - timeout + TimeSpan.FromSeconds(5));
    }
}
