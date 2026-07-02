using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Application.Abstractions.Persistence;
using OrderHub.NotificationService.Application.Notifications;
using OrderHub.NotificationService.Domain.Orders;

namespace OrderHub.NotificationService.UnitTests.Notifications;

/// <summary>
/// <see cref="CartAbandonmentReminderJob"/> birim testleri.
/// Mutlu yol (hatırlatma gönderilir), idempotency (zaten gönderildiyse skip), durum guard'ları (Paid/Cancelled skip)
/// ve null projeksiyon (projection yok → skip) senaryoları.
/// </summary>
public sealed class CartAbandonmentReminderJobTests
{
    private readonly Mock<INotificationOrderRepository> _repository = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly CartAbandonmentReminderJob _job;

    public CartAbandonmentReminderJobTests()
    {
        _job = new CartAbandonmentReminderJob(
            _repository.Object,
            _emailSender.Object,
            NullLogger<CartAbandonmentReminderJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_PaidProjection_NoEmailSentNoSaveChanges()
    {
        var orderId = Guid.NewGuid();
        var projection = CreateProjection(orderId);
        projection.MarkPaid(DateTime.UtcNow);

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(It.IsAny<NotificationEmailKind>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        projection.ReminderSentUtc.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledProjection_NoEmailSentNoSaveChanges()
    {
        var orderId = Guid.NewGuid();
        var projection = CreateProjection(orderId);
        projection.MarkCancelled(DateTime.UtcNow);

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(It.IsAny<NotificationEmailKind>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CreatedProjectionReminderNotSent_SendsEmailAndStampsReminderSentUtc()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var projection = CreateProjection(orderId, customerId);

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(NotificationEmailKind.CartAbandonmentReminder, customerId, orderId, It.IsAny<CancellationToken>()),
            Times.Once);
        projection.ReminderSentUtc.Should().NotBeNull("MarkReminderSent must be called");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ConfirmedProjectionReminderNotSent_SendsEmailAndStampsReminderSentUtc()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var projection = CreateProjection(orderId, customerId);
        projection.MarkConfirmed(DateTime.UtcNow);

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(NotificationEmailKind.CartAbandonmentReminder, customerId, orderId, It.IsAny<CancellationToken>()),
            Times.Once);
        projection.ReminderSentUtc.Should().NotBeNull();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReminderAlreadySent_NoEmailSentNoSaveChanges()
    {
        var orderId = Guid.NewGuid();
        var projection = CreateProjection(orderId);
        projection.MarkReminderSent(DateTime.UtcNow.AddHours(-1));

        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(It.IsAny<NotificationEmailKind>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectionNotFound_NoEmailSentNoSaveChanges()
    {
        var orderId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProjection?)null);

        await _job.ExecuteAsync(orderId, CancellationToken.None);

        _emailSender.Verify(
            e => e.SendAsync(It.IsAny<NotificationEmailKind>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OrderProjection CreateProjection(Guid orderId, Guid? customerId = null) =>
        OrderProjection.Create(
            orderId,
            customerId ?? Guid.NewGuid(),
            total: 100m,
            currency: "TRY",
            createdAtUtc: DateTime.UtcNow,
            lastUpdatedUtc: DateTime.UtcNow);
}
