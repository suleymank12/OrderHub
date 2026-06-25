using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Payments;
using OrderHub.Inbox;
using OrderHub.Inbox.Consuming;
using OrderHub.OrderService.Application;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Messaging;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Messaging;

/// <summary>
/// Faz 3 Adım 3d-2 — Inbox dedup (ADR-0005), gerçek SQL + in-memory harness (filter consume-pipe'a bağlı).
/// 3c-3'teki aggregate-guard testinden FARK: guard consume EDİP no-op yapar; inbox consume'a ULAŞMADAN keser.
/// Pozitif: yeni mesaj → consumer çalışır (order Paid) + inbox satırı atomik yazılır. Negatif (dedup):
/// önceden işlenmiş (inbox'ta kayıtlı) mesaj → consumer SKIP → order Confirmed kalır. Ayrıca composite-PK
/// concurrency backstop (DbUpdateException). Seed/commit edilen satırlar finally'de temizlenir.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InboxIdempotencyTests(SqlServerContainerFixture fixture)
{
    private static readonly string PaymentSucceededType = typeof(PaymentSucceededIntegrationEvent).FullName!;

    [Fact]
    public async Task FreshMessage_PassesFilter_ConsumerRunsAndInboxRecordWrittenAtomically()
    {
        var orderId = await SeedConfirmedOrderAsync();
        var eventId = Guid.NewGuid();
        try
        {
            await using var provider = BuildProviderWithInbox();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(NewPaymentSucceeded(eventId, orderId));

                var order = await WaitForOrderPaidAsync(orderId);
                order.Status.Should().Be(OrderStatus.Paid);

                // Atomiklik (Karar 3): order Paid ile birlikte inbox satırı da commit edilmiş olmalı.
                (await InboxRowExistsAsync(eventId)).Should().BeTrue("filter mesajı geçirdi + inbox kaydını yazdı");
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            await CleanupAsync(orderId, eventId);
        }
    }

    [Fact]
    public async Task AlreadyInInbox_FilterSkipsConsumer_OrderStaysConfirmed()
    {
        var orderId = await SeedConfirmedOrderAsync();
        var eventId = Guid.NewGuid();
        await SeedInboxRecordAsync(eventId); // mesaj "daha önce işlenmiş" → filter skip etmeli.
        try
        {
            await using var provider = BuildProviderWithInbox();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(NewPaymentSucceeded(eventId, orderId));
                (await harness.Published.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();

                // Dedup: consumer ÇALIŞMAMALI → order Confirmed kalır. Bounded gözlem (taze mesaj ~ms'de Paid
                // yapardı — pozitif test bunu kanıtlıyor; burada Paid'e GEÇMEMESİ inbox skip'inin kanıtı).
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    (await LoadOrderAsync(orderId)).Status.Should().Be(
                        OrderStatus.Confirmed, "inbox duplicate'i consumer'a ulaşmadan kesmeli (handler çalışmamalı)");
                    await Task.Delay(100);
                }
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            await CleanupAsync(orderId, eventId);
        }
    }

    [Fact]
    public async Task InboxMessages_DuplicateCompositeKey_ThrowsDbUpdateException()
    {
        var messageId = Guid.NewGuid();
        try
        {
            await using (var context = fixture.CreateContext())
            {
                context.InboxMessages.Add(InboxMessage.Create(messageId, PaymentSucceededType));
                await context.SaveChangesAsync();
            }

            // Aynı (MessageId, MessageType) ikinci insert → composite PK ihlali (ADR-0005 Karar 5 backstop).
            await using var second = fixture.CreateContext();
            second.InboxMessages.Add(InboxMessage.Create(messageId, PaymentSucceededType));
            var act = async () => await second.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await using var context = fixture.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM InboxMessages WHERE MessageId = {messageId}");
        }
    }

    private static PaymentSucceededIntegrationEvent NewPaymentSucceeded(Guid eventId, Guid orderId) => new()
    {
        Id = eventId,
        OccurredOnUtc = DateTime.UtcNow,
        OrderId = orderId,
        ExternalTransactionId = "MOCK-TX-INBOX",
    };

    private async Task<Guid> SeedConfirmedOrderAsync()
    {
        await using var context = fixture.CreateContext();
        var order = OrderTestData.NewOrder();
        order.Confirm();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task SeedInboxRecordAsync(Guid eventId)
    {
        await using var context = fixture.CreateContext();
        context.InboxMessages.Add(InboxMessage.Create(eventId, PaymentSucceededType));
        await context.SaveChangesAsync();
    }

    private async Task<Order> LoadOrderAsync(Guid orderId)
    {
        await using var context = fixture.CreateContext();
        return await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    private async Task<Order> WaitForOrderPaidAsync(Guid orderId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var order = await LoadOrderAsync(orderId);
            if (order.Status == OrderStatus.Paid)
            {
                return order;
            }

            await Task.Delay(200, timeout.Token);
        }
    }

    private async Task<bool> InboxRowExistsAsync(Guid eventId)
    {
        await using var context = fixture.CreateContext();
        return await context.InboxMessages.AsNoTracking()
            .AnyAsync(m => m.MessageId == eventId && m.MessageType == PaymentSucceededType);
    }

    private async Task CleanupAsync(Guid orderId, Guid eventId)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM InboxMessages WHERE MessageId = {eventId}");
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {orderId}");
    }

    private ServiceProvider BuildProviderWithInbox()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        RemoveMassTransitRegistrations(services);
        services.RemoveAll<IHostedService>(); // RecurringJobRegistrar (Hangfire) + OutboxProcessor harness'te gereksiz.

        services.AddMassTransitTestHarness(busConfigurator =>
        {
            busConfigurator.AddConsumer<PaymentSucceededIntegrationEventConsumer>();
            busConfigurator.AddConsumer<PaymentFailedIntegrationEventConsumer>();
            busConfigurator.UsingInMemory((context, cfg) =>
            {
                // Production ile aynı: inbox dedup filter consume-pipe'a, endpoint'lerden önce.
                cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private static void RemoveMassTransitRegistrations(ServiceCollection services)
    {
        var descriptors = services
            .Where(d =>
                IsMassTransitType(d.ServiceType) ||
                IsMassTransitType(d.ImplementationType) ||
                IsMassTransitType(d.ImplementationInstance?.GetType()))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsMassTransitType(Type? type) =>
        type?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;
}
