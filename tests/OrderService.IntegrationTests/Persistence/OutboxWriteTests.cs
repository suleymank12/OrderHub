using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OrderHub.Contracts.Orders;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// Outbox write-only çekirdeği — <b>gerçek production DI</b> (<see cref="DependencyInjection.AddInfrastructure"/>)
/// üzerinden, Faz 5 5d-5b cutover ile güncel:
/// <list type="bullet">
/// <item>Confirm() → OrderConfirmed <b>1:1</b> outbox (OrderConfirmedIntegrationEvent, Kafka, Ordinal 0). ★ ProcessPayment
///   map'i KALDIRILDI — ödemeyi artık SAGA publish eder.</item>
/// <item>Create() → OrderCreated <b>1:N</b> outbox: OrderCreatedIntegrationEvent (Kafka, Ordinal 0) +
///   OrderPlacedIntegrationEvent (RabbitMQ saga trigger, Ordinal 1, payload dolu).</item>
/// <item>Outbox satırı domain order ile <b>aynı transaction</b>: rollback → ikisi de yok; commit → ikisi de var.</item>
/// <item><b>Clear-invariant:</b> pre-commit outbox READ-ONLY (domain event'i CLEAR ETMEZ) → post-commit in-process
///   dispatch çalışır + event'ler temizlenir → Faz 2 zinciri sağ (ADR-0002 Faz 3 Karar 2).</item>
/// </list>
/// İzolasyon: rollback testleri commit etmez; commit testi yazdığını siler (paralelleştirme kapalı, seri).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxWriteTests(SqlServerContainerFixture fixture)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SaveChanges_OrderConfirmed_WritesOnlyKafkaOutbox_AndDispatchStillRuns()
    {
        var publisherMock = LoosePublisher();

        await using var provider = BuildProvider(publisherMock.Object);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        order.Confirm();
        // EventId'yi SAVE'den ÖNCE yakala: dispatcher post-commit DomainEvents'i temizler.
        var confirmedEventId = order.DomainEvents.OfType<OrderConfirmed>().Single().EventId;

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // 5d-5b cutover: OrderConfirmed artık 1:1 → yalnız OrderConfirmedIntegrationEvent (Kafka, Ordinal 0).
        // ProcessPayment map'i kaldırıldı (ödemeyi saga publish eder → çift ProcessPayment yok).
        var confirmedRows = await context.OutboxMessages
            .Where(message => message.Id == confirmedEventId)
            .OrderBy(message => message.Ordinal)
            .ToListAsync();

        var row = confirmedRows.Should().ContainSingle().Which;
        row.Ordinal.Should().Be(0);
        row.Type.Should().Be(typeof(OrderConfirmedIntegrationEvent).AssemblyQualifiedName);
        row.Id.Should().Be(confirmedEventId);
        row.ProcessedOnUtc.Should().BeNull();
        row.RetryCount.Should().Be(0);

        // Outbox (pre-commit) OKUDU ama CLEAR ETMEDİ → dispatcher (post-commit) OrderConfirmed'ı publish edebildi.
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n => (n as DomainEventNotification<OrderConfirmed>) != null),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "pre-commit outbox domain event'i clear etmemeli; post-commit dispatch çalışmalı");
    }

    [Fact]
    public async Task SaveChanges_TransactionRolledBack_NeitherOrderNorOutboxPersist()
    {
        var orderId = Guid.Empty;
        var eventId = Guid.Empty;

        await using (var provider = BuildProvider(NullPublisher.Instance))
        {
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();

            var order = OrderTestData.NewOrder();
            order.Confirm();
            orderId = order.Id;
            eventId = order.DomainEvents.OfType<OrderConfirmed>().Single().EventId;

            context.Orders.Add(order);
            await context.SaveChangesAsync();

            // Aynı transaction içinde her ikisi de görünür (atomik yazıldı). OrderConfirmed → 1:1 (1 outbox satırı, Kafka).
            (await context.Orders.CountAsync(o => o.Id == orderId)).Should().Be(1);
            (await context.OutboxMessages.CountAsync(m => m.Id == eventId)).Should().Be(1);

            // transaction commit EDİLMEZ → dispose rollback eder.
        }

        // Taze context (transaction dışı): rollback ikisini birden geri aldı.
        await using var verifyProvider = BuildProvider(NullPublisher.Instance);
        using var verifyScope = verifyProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<OrderDbContext>();

        (await verifyContext.Orders.AnyAsync(o => o.Id == orderId)).Should().BeFalse("rollback → order yok");
        (await verifyContext.OutboxMessages.AnyAsync(m => m.Id == eventId)).Should().BeFalse("rollback → outbox yok");
    }

    [Fact]
    public async Task SaveChanges_TransactionCommitted_BothOrderAndOutboxPersist()
    {
        var orderId = Guid.Empty;
        var eventId = Guid.Empty;

        try
        {
            await using var provider = BuildProvider(NullPublisher.Instance);
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();

            var order = OrderTestData.NewOrder();
            order.Confirm();
            orderId = order.Id;
            eventId = order.DomainEvents.OfType<OrderConfirmed>().Single().EventId;

            context.Orders.Add(order);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            context.ChangeTracker.Clear();

            (await context.Orders.AnyAsync(o => o.Id == orderId)).Should().BeTrue("commit → order kalıcı");
            (await context.OutboxMessages.AnyAsync(m => m.Id == eventId)).Should().BeTrue("commit → outbox kalıcı");
        }
        finally
        {
            // Commit edildi → izolasyonu korumak için yazılan satırları temizle (FK yok, sıra serbest).
            await CleanupAsync(orderId, eventId);
        }
    }

    [Fact]
    public async Task SaveChanges_OrderCreated_FansOutToKafkaAndOrderPlaced_AndInProcessDispatchStillRuns()
    {
        // ★ 5d-5b cutover: OrderCreated 1:N → OrderCreatedIntegrationEvent (Kafka/Analytics, Ordinal 0) +
        // OrderPlacedIntegrationEvent (RabbitMQ saga trigger, Ordinal 1). Pre-commit outbox READ-ONLY → post-commit
        // dispatcher hâlâ publish+clear eder (Faz 2 OrderCreated→Hangfire zinciri BOZULMAZ).
        var publisherMock = LoosePublisher();

        await using var provider = BuildProvider(publisherMock.Object);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder(); // yalnızca OrderCreated.
        var createdEventId = order.DomainEvents.OfType<OrderCreated>().Single().EventId;

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // 1) Outbox: OrderCreated → 2 satır (Kafka Ordinal 0 + OrderPlaced Ordinal 1); ikisi de Id == EventId.
        var rows = await context.OutboxMessages
            .Where(m => m.Id == createdEventId)
            .OrderBy(m => m.Ordinal)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Ordinal.Should().Be(0);
        rows[0].Type.Should().Be(typeof(OrderCreatedIntegrationEvent).AssemblyQualifiedName);
        rows[1].Ordinal.Should().Be(1);
        rows[1].Type.Should().Be(typeof(OrderPlacedIntegrationEvent).AssemblyQualifiedName);

        // 2) OrderPlaced payload (saga trigger): CustomerId/Items/Amount/Currency dolu.
        var placed = JsonSerializer.Deserialize<OrderPlacedIntegrationEvent>(rows[1].Payload, PayloadOptions);
        placed.Should().NotBeNull();
        placed!.OrderId.Should().Be(order.Id);
        placed.CustomerId.Should().Be(order.CustomerId);
        placed.Amount.Should().Be(order.Total.Amount);
        placed.Currency.Should().Be(order.Total.Currency.ToString());
        placed.Items.Should().NotBeEmpty("saga ReserveStock için kalem listesi");
        placed.Items.Should().OnlyContain(item => item.ProductId != Guid.Empty && item.Quantity > 0);

        // 3) In-process dispatch ÇALIŞTI: DispatchDomainEventsInterceptor OrderCreated'ı publish etti (Faz 2 zinciri sağ).
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n => (n as DomainEventNotification<OrderCreated>) != null),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Faz 2 in-process dispatch (OrderCreated → Hangfire) kırılmamalı");

        // 4) Domain event'ler temizlendi → pre-commit outbox DEĞİL, post-commit dispatcher clear etti.
        order.DomainEvents.Should().BeEmpty();
    }

    private static Mock<IPublisher> LoosePublisher()
    {
        var publisherMock = new Mock<IPublisher>(MockBehavior.Loose);
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisherMock;
    }

    // Gerçek production DI: AddInfrastructure outbox writer + interceptor'ları (production map ile) kurar.
    // IPublisher AddInfrastructure'da kayıtlı değil (Api/AddApplication'da) → DispatchDomainEventsInterceptor
    // için burada sağlanır.
    private ServiceProvider BuildProvider(IPublisher publisher)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddInfrastructure(configuration)
            .AddSingleton(publisher)
            .BuildServiceProvider();
    }

    private async Task CleanupAsync(Guid orderId, Guid eventId)
    {
        await using var provider = BuildProvider(NullPublisher.Instance);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OutboxMessages WHERE Id = {eventId}");
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {orderId}");
    }
}
