using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Abstractions.Scheduling;
using OrderHub.OrderService.Application.Orders.Configuration;
using OrderHub.OrderService.Application.Orders.EventHandlers;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.EventHandlers;

/// <summary>
/// <see cref="OrderCreatedDomainEventHandler"/> davranış testleri: yeni sipariş oluşturulduğunda
/// doğru timeout ile <see cref="IOrderTimeoutScheduler.ScheduleCancellation"/> çağrısı yapılır.
/// Handler Hangfire'ı bilmez — port üzerinden gecikmeli iptal planlar (DIP, ADR-0003).
/// </summary>
public sealed class OrderCreatedDomainEventHandlerTests
{
    private readonly Mock<IOrderTimeoutScheduler> _scheduler = new(MockBehavior.Strict);

    private OrderCreatedDomainEventHandler BuildSut(OrderTimeoutOptions options) =>
        new(
            _scheduler.Object,
            Options.Create(options),
            NullLogger<OrderCreatedDomainEventHandler>.Instance);

    private static DomainEventNotification<OrderCreated> BuildNotification(Guid orderId)
    {
        // OrderCreated için geçerli Money gerekiyor; MoneyFactory'nin default'u yeterli. Items handler'ı
        // ilgilendirmez (yalnız OrderId okur) → boş liste yeterli.
        var money = MoneyFactory.Default();
        var domainEvent = new OrderCreated(orderId, Guid.NewGuid(), money, []);
        return new DomainEventNotification<OrderCreated>(domainEvent);
    }

    // -------------------------------------------------------------------------
    // Senaryo 1: Yapılandırılmış timeout ile iptal job'u planlanır
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_OrderCreated_SchedulesCancellationWithConfiguredTimeout()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var timeout = TimeSpan.FromMinutes(30);
        var options = new OrderTimeoutOptions { UnpaidTimeout = timeout };

        _scheduler.Setup(s => s.ScheduleCancellation(orderId, timeout));

        var sut = BuildSut(options);
        var notification = BuildNotification(orderId);

        // Act
        await sut.Handle(notification, CancellationToken.None);

        // Assert — port tam bir kez, doğru argümanlarla çağrıldı.
        _scheduler.Verify(s => s.ScheduleCancellation(orderId, timeout), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Default timeout (15 dk) ile çalışır
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_OrderCreated_UsesDefaultTimeoutWhenNotOverridden()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var defaultOptions = new OrderTimeoutOptions(); // UnpaidTimeout = 15 dk (default)

        _scheduler.Setup(s => s.ScheduleCancellation(orderId, TimeSpan.FromMinutes(15)));

        var sut = BuildSut(defaultOptions);
        var notification = BuildNotification(orderId);

        // Act
        await sut.Handle(notification, CancellationToken.None);

        // Assert
        _scheduler.Verify(
            s => s.ScheduleCancellation(orderId, TimeSpan.FromMinutes(15)),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Notification'dan OrderId doğru okunuyor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_OrderCreated_PassesNotificationOrderIdToScheduler()
    {
        // Arrange: iki farklı orderId ile iki notification — her biri doğru id ile planlanmalı.
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var options = new OrderTimeoutOptions { UnpaidTimeout = TimeSpan.FromMinutes(15) };

        _scheduler.Setup(s => s.ScheduleCancellation(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));

        var sut = BuildSut(options);

        // Act
        await sut.Handle(BuildNotification(firstId), CancellationToken.None);
        await sut.Handle(BuildNotification(secondId), CancellationToken.None);

        // Assert — her orderId ayrı ayrı ve tam bir kez planlandı.
        _scheduler.Verify(s => s.ScheduleCancellation(firstId, It.IsAny<TimeSpan>()), Times.Once);
        _scheduler.Verify(s => s.ScheduleCancellation(secondId, It.IsAny<TimeSpan>()), Times.Once);
    }
}
