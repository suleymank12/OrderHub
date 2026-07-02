using Microsoft.Extensions.Logging.Abstractions;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Infrastructure.Notifications;

namespace OrderHub.NotificationService.UnitTests.Notifications;

/// <summary>
/// <see cref="MockEmailSender"/> birim testleri.
/// Gönderilen e-postaların <see cref="MockEmailSender.Sent"/> listesine doğru kaydedildiğini doğrular.
/// </summary>
public sealed class MockEmailSenderTests
{
    [Fact]
    public async Task SendAsync_TwiceDifferentKinds_SentContainsBothRecordsWithCorrectValues()
    {
        var sender = new MockEmailSender(NullLogger<MockEmailSender>.Instance);

        var orderId1 = Guid.NewGuid();
        var customerId1 = Guid.NewGuid();
        await sender.SendAsync(NotificationEmailKind.OrderConfirmed, customerId1, orderId1, CancellationToken.None);

        var orderId2 = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();
        await sender.SendAsync(NotificationEmailKind.CartAbandonmentReminder, customerId2, orderId2, CancellationToken.None);

        sender.Sent.Should().HaveCount(2);

        sender.Sent[0].Kind.Should().Be(NotificationEmailKind.OrderConfirmed);
        sender.Sent[0].OrderId.Should().Be(orderId1);
        sender.Sent[0].CustomerId.Should().Be(customerId1);

        sender.Sent[1].Kind.Should().Be(NotificationEmailKind.CartAbandonmentReminder);
        sender.Sent[1].OrderId.Should().Be(orderId2);
        sender.Sent[1].CustomerId.Should().Be(customerId2);
    }

    [Fact]
    public async Task SendAsync_ReturnedImmediately_DoesNotThrow()
    {
        var sender = new MockEmailSender(NullLogger<MockEmailSender>.Instance);
        var orderId = Guid.NewGuid();

        // Task.CompletedTask dönmeli → await bloklamaz.
        var task = sender.SendAsync(NotificationEmailKind.OrderConfirmed, Guid.NewGuid(), orderId, CancellationToken.None);
        task.IsCompleted.Should().BeTrue("MockEmailSender.SendAsync must return a completed task (no real I/O)");
        await task;
    }
}
