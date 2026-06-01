using MediatR;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="OrderHub.OrderService.Infrastructure.Persistence.Interceptors.DispatchDomainEventsInterceptor"/>'ın
/// gerçek SQL Server container'ına karşı davranışları:
/// - SavedChangesAsync, aggregate'in her domain event'ini bir kez ve doğru
///   <see cref="DomainEventNotification{TDomainEvent}"/> wrapper'ı ile publish eder.
/// - Publish tamamlandıktan sonra aggregate'in DomainEvents koleksiyonu boş kalır
///   (clear ≥ dispatch — ADR-0002).
/// Her test kendi transaction'ında izole (rollback): DB'de kirli veri bırakmaz.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DispatchDomainEventsInterceptorTests(SqlServerContainerFixture fixture)
{
    // -------------------------------------------------------------------------
    // Senaryo 1: Tek event — Order.Create → OrderCreated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_OrderCreated_PublishesNotificationExactlyOnce()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>(MockBehavior.Strict);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var context = fixture.CreateContext(publisherMock.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        var expectedOrderId = order.Id;

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert — doğru generic tip ve OrderId ile tam bir kez çağrıldı.
        // Expression tree kısıtı: 'is' pattern matching Expression tree'de desteklenmiyor (CS8122).
        // Cast + null check ile eşdeğer boolean lambda kullanılır.
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n =>
                    (n as DomainEventNotification<OrderCreated>) != null &&
                    ((DomainEventNotification<OrderCreated>)n).DomainEvent.OrderId == expectedOrderId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Başka hiçbir Publish çağrısı olmamalı (Strict mock bunu otomatik zorlar;
        // Times.Exactly(1) Verify ile de açık biçimde teyit edilir).
        publisherMock.Verify(
            p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task SaveChangesAsync_OrderCreated_ClearsDomainEventsAfterPublish()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>(MockBehavior.Loose);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var context = fixture.CreateContext(publisherMock.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        order.DomainEvents.Should().NotBeEmpty("Order.Create OrderCreated yükseltmiş olmalı");

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert — publish'ten sonra temizlendi (clear ≥ dispatch: çift yayın olmaz).
        order.DomainEvents.Should().BeEmpty(
            "DispatchDomainEventsInterceptor publish tamamlandıktan sonra ClearDomainEvents çağırır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: İki event — Create + Confirm aynı SaveChanges → her ikisi publish
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_OrderCreatedAndConfirmed_PublishesBothNotificationsOnce()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>(MockBehavior.Strict);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var context = fixture.CreateContext(publisherMock.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        order.Confirm(); // OrderCreated + OrderConfirmed → aynı save'de iki event
        var expectedOrderId = order.Id;

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert — OrderCreated tam bir kez
        // Expression tree kısıtı: 'is' pattern matching Expression tree'de desteklenmiyor (CS8122).
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n =>
                    (n as DomainEventNotification<OrderCreated>) != null &&
                    ((DomainEventNotification<OrderCreated>)n).DomainEvent.OrderId == expectedOrderId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "OrderCreated bildirimi tam bir kez yayınlanmalı");

        // Assert — OrderConfirmed tam bir kez
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n =>
                    (n as DomainEventNotification<OrderConfirmed>) != null &&
                    ((DomainEventNotification<OrderConfirmed>)n).DomainEvent.OrderId == expectedOrderId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "OrderConfirmed bildirimi tam bir kez yayınlanmalı");

        // Toplamda tam iki çağrı (Strict mock yabancı çağrıyı zaten reddeder).
        publisherMock.Verify(
            p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SaveChangesAsync_OrderCreatedAndConfirmed_ClearsDomainEventsAfterPublish()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>(MockBehavior.Loose);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var context = fixture.CreateContext(publisherMock.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        order.Confirm();
        order.DomainEvents.Should().HaveCount(2, "Create + Confirm → iki event bekleniyordu");

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert
        order.DomainEvents.Should().BeEmpty(
            "Her iki event publish edildikten sonra koleksiyon temizlenmeli");
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Notification wrapper doğru reflection ile sarılıyor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_OrderCreated_WrapsEventWithCorrectConcreteGenericType()
    {
        // Arrange — yakalanan notification somut tipin DomainEventNotification<OrderCreated>
        // olduğunu doğrular (INotification'a upcasting reflection'ı gizlemesin diye).
        INotification? captured = null;

        var publisherMock = new Mock<IPublisher>(MockBehavior.Loose);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((n, _) => captured = n)
            .Returns(Task.CompletedTask);

        await using var context = fixture.CreateContext(publisherMock.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert — yakalanan nesne kesinlikle DomainEventNotification<OrderCreated> olmalı.
        captured.Should().NotBeNull();
        captured.Should().BeOfType<DomainEventNotification<OrderCreated>>(
            "WrapAsNotification reflection ile somut DomainEventNotification<OrderCreated> üretmeli; " +
            "DomainEventNotification<IDomainEvent> gibi açık bir tip üretilmemeli");

        var notification = (DomainEventNotification<OrderCreated>)captured!;
        notification.DomainEvent.OrderId.Should().Be(order.Id);
    }
}
