using Microsoft.EntityFrameworkCore;
using OrderHub.Contracts.Orders;
using OrderHub.NotificationService.Application.Abstractions.Notifications;
using OrderHub.NotificationService.Domain.Orders;
using OrderHub.NotificationService.Infrastructure.Notifications;
using OrderHub.NotificationService.IntegrationTests.Fixtures;

namespace OrderHub.NotificationService.IntegrationTests.Notifications;

/// <summary>
/// Faz 5 5f-3 — cart-abandonment <b>uçtan uca</b>, gerçek Kafka + SQL + <b>gerçek Hangfire</b> (tam Program).
/// Gerçek delayed job fire içerdiğinden en flaky-eğilimli adım → 5d-7/5e-3 dersi: <b>flaky/sahte-yeşil YASAK</b>.
/// <list type="number">
///   <item><b>Happy (unpaid → reminder):</b> pozitif sinyal — MockEmailSender recorder'da reminder görünene kadar
///     bounded-wait (Task.Delay+assert YOK).</item>
///   <item><b>Guard (paid → email yok):</b> negatif assertion tuzağı DETERMİNİSTİK çözülür — (a) OrderPaid
///     projeksiyona uygulandığını (Status=Paid) fire'dan ÖNCE doğrula, (b) reminder job'un GERÇEKTEN fire olup
///     <b>Succeeded</b>'e ulaştığını Hangfire monitoring ile doğrula (pozitif "job çalıştı" sinyali), (c) SONRA
///     reminder email YOK assert et → "job çalıştı ama guard sustu" = GERÇEK guard kanıtı ("fire olmadı" değil).</item>
/// </list>
/// Guard birimi ayrıca 5f-2 unit'te izole kanıtlı (Paid/Cancelled/ReminderSentUtc → no-op).
/// </summary>
[Collection(NotificationAppCollection.Name)]
public sealed class CartAbandonmentEndToEndTests(NotificationAppFixture app)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task UnpaidOrder_ReminderJobFires_SendsCartAbandonmentEmail()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await app.ProduceAsync(OrderCreated(orderId, customerId));

        // ★ Pozitif sinyal: reminder job kısa delay sonrası fire eder → unpaid sipariş için email gönderir.
        var reminder = await WaitForReminderEmailAsync(orderId, Timeout);
        reminder.Should().NotBeNull("kısa delay sonrası reminder job fire olup unpaid sipariş için email göndermeli");
        reminder!.CustomerId.Should().Be(customerId);

        // SQL'den ReminderSentUtc doğrula (job gerçekten stamp'ledi → idempotency kaydı).
        var projection = await ReadProjectionAsync(orderId);
        projection.ReminderSentUtc.Should().NotBeNull();
        projection.Status.Should().Be(OrderProjectionStatus.Created, "sipariş ödenmedi → hâlâ Created");
    }

    [Fact]
    public async Task PaidOrder_ReminderJobFiresButGuardSuppresses_NoEmail()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await app.ProduceAsync(OrderCreated(orderId, customerId)); // reminder job schedule (kısa delay)
        await app.ProduceAsync(OrderPaid(orderId));

        // 1) ★ Job fire'dan ÖNCE projeksiyon Paid GARANTİ (consume ≪ delay → deterministik sıra).
        await WaitForStatusAsync(orderId, OrderProjectionStatus.Paid, Timeout);

        // 2) ★ Reminder job GERÇEKTEN fire olup Succeeded'e ulaştı → "job çalıştı" pozitif sinyali (negatif assertion'ı meşru kılar).
        await WaitForReminderJobSucceededAsync(orderId, Timeout);

        // 3) ★ Job çalıştı AMA guard sustu → CartAbandonment reminder YOK (gerçek guard kanıtı; "fire olmadı" sahte-yeşili değil).
        ReminderEmailsFor(orderId).Should().BeEmpty("reminder job Succeeded ama sipariş Paid → guard email'i bastırdı");
        (await ReadProjectionAsync(orderId)).ReminderSentUtc.Should().BeNull("guard no-op → ReminderSentUtc stamp yok");
    }

    private async Task<SentEmail?> WaitForReminderEmailAsync(Guid orderId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            var reminders = ReminderEmailsFor(orderId);
            if (reminders.Count > 0)
            {
                return reminders[0];
            }

            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(200, cts.Token);
        }
    }

    private List<SentEmail> ReminderEmailsFor(Guid orderId) =>
        app.EmailRecorder.Sent
            .Where(email => email.Kind == NotificationEmailKind.CartAbandonmentReminder && email.OrderId == orderId)
            .ToList();

    private async Task WaitForStatusAsync(Guid orderId, OrderProjectionStatus status, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            await using (var context = app.CreateContext())
            {
                var projection = await context.OrderProjections.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.OrderId == orderId, cts.Token);
                if (projection is not null && projection.Status == status)
                {
                    return;
                }
            }

            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(200, cts.Token);
        }
    }

    // Hangfire monitoring: reminder job (arg = orderId) Succeeded state'ine ulaşana kadar bekle → job GERÇEKTEN çalıştı.
    private async Task WaitForReminderJobSucceededAsync(Guid orderId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var monitoring = app.JobStorage.GetMonitoringApi();
        var needle = orderId.ToString();
        while (true)
        {
            var succeeded = monitoring.SucceededJobs(0, 500);
            if (succeeded.Any(entry => entry.Value.Job is not null
                && entry.Value.Job.Args.Any(arg => string.Equals(arg?.ToString(), needle, StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }

            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(250, cts.Token);
        }
    }

    private async Task<OrderProjection> ReadProjectionAsync(Guid orderId)
    {
        await using var context = app.CreateContext();
        return await context.OrderProjections.AsNoTracking().SingleAsync(p => p.OrderId == orderId);
    }

    private static OrderCreatedIntegrationEvent OrderCreated(Guid orderId, Guid customerId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 100m,
            Currency = "TRY",
        };

    private static OrderPaidIntegrationEvent OrderPaid(Guid orderId) =>
        new() { Id = Guid.NewGuid(), OccurredOnUtc = DateTime.UtcNow, OrderId = orderId };
}
