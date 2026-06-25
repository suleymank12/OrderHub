using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.EventBus;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.Infrastructure.Persistence.Interceptors;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;
using OrderHub.Outbox;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// Faz 4 Adım 4a — composite outbox PK (Id, Ordinal) + registry 1:N fan-out (ADR-0006 Karar 4). Bir domain
/// olayı (OrderConfirmed) <b>iki</b> factory'ye map'lenince interceptor 2 outbox satırı yazar (Ordinal 0/1,
/// ikisi de Id == EventId); composite PK (Id, 0) + (Id, 1)'e izin verir, aynı (Id, Ordinal)'i reddeder.
/// Mevcut 1:1 davranış (OutboxWriteTests) korunur — bu test 1:N'in YENİ yolunu gerçek SQL'de doğrular.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxFanOutTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Interceptor_DomainEventMappedToTwoTargets_WritesTwoRows_Ordinal0And1_BothIdEqualsEventId()
    {
        var orderId = Guid.Empty;
        var eventId = Guid.Empty;
        try
        {
            await using var provider = BuildProviderWithFanOut();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var order = OrderTestData.NewOrder();
            order.Confirm();
            orderId = order.Id;
            eventId = order.DomainEvents.OfType<OrderConfirmed>().Single().EventId;

            context.Orders.Add(order);
            await context.SaveChangesAsync(); // pre-commit interceptor → 1:N fan-out (2 satır)
            context.ChangeTracker.Clear();

            var rows = await context.Set<OutboxMessage>()
                .Where(message => message.Id == eventId)
                .OrderBy(message => message.Ordinal)
                .ToListAsync();

            rows.Should().HaveCount(2, "OrderConfirmed iki factory'ye map'lendi → 2 outbox satırı (1:N)");
            rows[0].Ordinal.Should().Be(0);
            rows[1].Ordinal.Should().Be(1);
            rows.Should().OnlyContain(r => r.Id == eventId, "tüm fan-out hedefleri aynı EventId'yi taşır (ADR-0002 Karar 4 korunur)");
            rows.Select(r => r.Type).Should().OnlyHaveUniqueItems("iki farklı integration event tipi (farklı hedefler)");
        }
        finally
        {
            await CleanupAsync(orderId, eventId);
        }
    }

    [Fact]
    public async Task OutboxMessages_CompositePk_AcceptsSameIdDifferentOrdinal_RejectsDuplicateOrdinal()
    {
        var id = Guid.NewGuid();
        const string type = "OutboxFanOutTests.CompositePkProbe";
        try
        {
            // (Id, 0) + (Id, 1): aynı Id, farklı Ordinal → composite PK izin verir.
            await using (var context = fixture.CreateContext())
            {
                context.Set<OutboxMessage>().Add(OutboxMessage.Create(id, type, "{}", DateTime.UtcNow, ordinal: 0));
                context.Set<OutboxMessage>().Add(OutboxMessage.Create(id, type, "{}", DateTime.UtcNow, ordinal: 1));
                await context.SaveChangesAsync();
            }

            // Aynı (Id, 0) ikinci insert → composite PK ihlali.
            await using var duplicate = fixture.CreateContext();
            duplicate.Set<OutboxMessage>().Add(OutboxMessage.Create(id, type, "{}", DateTime.UtcNow, ordinal: 0));
            var act = async () => await duplicate.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await using var context = fixture.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OutboxMessages WHERE Id = {id}");
        }
    }

    // Gerçek OrderDbContext (outbox interceptor dahil) + OrderConfirmed'ı İKİ test integration event'ine map eden
    // özel registry. AddInfrastructure'ın 1:1 map'i yerine fan-out map → 1:N yolunu izole eder.
    private ServiceProvider BuildProviderWithFanOut()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPublisher>(NullPublisher.Instance); // DispatchDomainEventsInterceptor bağımlılığı.
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddOutboxWriter(registry =>
        {
            registry.Map<OrderConfirmed>(domainEvent =>
                new FanOutEventA { Id = domainEvent.EventId, OccurredOnUtc = domainEvent.OccurredOnUtc });
            registry.Map<OrderConfirmed>(domainEvent =>
                new FanOutEventB { Id = domainEvent.EventId, OccurredOnUtc = domainEvent.OccurredOnUtc });
        });

        services.AddDbContext<OrderDbContext>((serviceProvider, options) => options
            .UseSqlServer(fixture.ConnectionString)
            .AddOutboxInterceptor(serviceProvider)
            .AddInterceptors(serviceProvider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        return services.BuildServiceProvider();
    }

    private async Task CleanupAsync(Guid orderId, Guid eventId)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OutboxMessages WHERE Id = {eventId}");
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {orderId}");
    }

    private sealed record FanOutEventA : IIntegrationEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
    }

    private sealed record FanOutEventB : IIntegrationEvent
    {
        public Guid Id { get; init; }
        public DateTime OccurredOnUtc { get; init; }
    }
}
