using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderHub.Contracts.Orders;
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
/// Inbox dedup (ADR-0005), gerçek SQL + in-memory harness (filter consume-pipe'a bağlı). Faz 5 5d-5a'da
/// vehicle <c>PaymentSucceeded</c>'dan <see cref="ConfirmOrderIntegrationEvent"/>'e <b>retarget</b> edildi
/// (Payment consumer'ları 5d-5b'de kalkacak; inbox mekanizması hayatta kalan bir consumer'la test edilmeye devam).
/// <see cref="SagaCommandConsumerTests"/>'teki aggregate-guard testinden FARK: guard consume EDİP no-op yapar;
/// inbox consume'a ULAŞMADAN keser. Pozitif: yeni mesaj → consumer çalışır (order Confirmed) + inbox satırı
/// atomik yazılır. Negatif (dedup): önceden işlenmiş mesaj → consumer SKIP → order Pending kalır. Ayrıca
/// composite-PK concurrency backstop (DbUpdateException). Seed/commit edilen satırlar finally'de temizlenir.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InboxIdempotencyTests(SqlServerContainerFixture fixture)
{
    private static readonly string ConfirmOrderType = typeof(ConfirmOrderIntegrationEvent).FullName!;

    [Fact]
    public async Task FreshMessage_PassesFilter_ConsumerRunsAndInboxRecordWrittenAtomically()
    {
        var orderId = await SeedPendingOrderAsync();
        var eventId = Guid.NewGuid();
        try
        {
            await using var provider = BuildProviderWithInbox();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(NewConfirmOrder(eventId, orderId));

                var order = await WaitForOrderConfirmedAsync(orderId);
                order.Status.Should().Be(OrderStatus.Confirmed);

                // Atomiklik (Karar 3): order Confirmed ile birlikte inbox satırı da commit edilmiş olmalı.
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
    public async Task AlreadyInInbox_FilterSkipsConsumer_OrderStaysPending()
    {
        var orderId = await SeedPendingOrderAsync();
        var eventId = Guid.NewGuid();
        await SeedInboxRecordAsync(eventId); // mesaj "daha önce işlenmiş" → filter skip etmeli.
        try
        {
            await using var provider = BuildProviderWithInbox();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            try
            {
                await harness.Bus.Publish(NewConfirmOrder(eventId, orderId));
                (await harness.Published.Any<ConfirmOrderIntegrationEvent>()).Should().BeTrue();

                // Dedup: consumer ÇALIŞMAMALI → order Pending kalır. Bounded gözlem (taze mesaj ~ms'de Confirmed
                // yapardı — pozitif test bunu kanıtlıyor; burada Confirmed'e GEÇMEMESİ inbox skip'inin kanıtı).
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    (await LoadOrderAsync(orderId)).Status.Should().Be(
                        OrderStatus.Pending, "inbox duplicate'i consumer'a ulaşmadan kesmeli (handler çalışmamalı)");
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
                context.InboxMessages.Add(InboxMessage.Create(messageId, ConfirmOrderType));
                await context.SaveChangesAsync();
            }

            // Aynı (MessageId, MessageType) ikinci insert → composite PK ihlali (ADR-0005 Karar 5 backstop).
            await using var second = fixture.CreateContext();
            second.InboxMessages.Add(InboxMessage.Create(messageId, ConfirmOrderType));
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

    private static ConfirmOrderIntegrationEvent NewConfirmOrder(Guid eventId, Guid orderId) => new()
    {
        Id = eventId,
        OccurredOnUtc = DateTime.UtcNow,
        OrderId = orderId,
    };

    private async Task<Guid> SeedPendingOrderAsync()
    {
        await using var context = fixture.CreateContext();
        var order = OrderTestData.NewOrder(); // Pending (Confirm edilmez → ConfirmOrder consumer bunu Confirmed yapar).
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task SeedInboxRecordAsync(Guid eventId)
    {
        await using var context = fixture.CreateContext();
        context.InboxMessages.Add(InboxMessage.Create(eventId, ConfirmOrderType));
        await context.SaveChangesAsync();
    }

    private async Task<Order> LoadOrderAsync(Guid orderId)
    {
        await using var context = fixture.CreateContext();
        return await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    private async Task<Order> WaitForOrderConfirmedAsync(Guid orderId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var order = await LoadOrderAsync(orderId);
            if (order.Status == OrderStatus.Confirmed)
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
            .AnyAsync(m => m.MessageId == eventId && m.MessageType == ConfirmOrderType);
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
            busConfigurator.AddConsumer<ConfirmOrderConsumer>();
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
