using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OrderHub.Contracts.Payments;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Domain.Orders.Events;
using OrderHub.OrderService.Infrastructure;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// Faz 3 Adım 2 (write-only) çekirdek doğrulamaları — <b>gerçek production DI</b> (<see cref="DependencyInjection.AddInfrastructure"/>)
/// üzerinden, yani gerçek OrderConfirmed→ProcessPayment map'i ve interceptor sırasıyla:
/// <list type="bullet">
/// <item>Confirm() → tam 1 OutboxMessage (ProcessPaymentIntegrationEvent, Id == EventId, payload doğru).</item>
/// <item>Outbox satırı domain order ile <b>aynı transaction</b>: rollback → ikisi de yok; commit → ikisi de var.</item>
/// <item><b>Clear-invariant:</b> Create() (OrderCreated registry'de YOK) → outbox'a hiçbir satır yazılmaz,
///   AMA in-process dispatch çalışır ve domain event'ler post-commit temizlenir → pre-commit outbox'ın
///   domain event'i CLEAR ETMEDİĞİNİN ve Faz 2 zincirinin sağ olduğunun kanıtı (ADR-0002 Faz 3 Karar 2).</item>
/// </list>
/// İzolasyon: rollback testleri commit etmez; commit testi yazdığını siler (paralelleştirme kapalı, seri).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxWriteTests(SqlServerContainerFixture fixture)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SaveChanges_OrderConfirmed_WritesSingleProcessPaymentOutboxMessage()
    {
        var publisherMock = LoosePublisher();

        await using var provider = BuildProvider(publisherMock.Object);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder();
        order.Confirm();
        // EventId'yi SAVE'den ÖNCE yakala: dispatcher post-commit DomainEvents'i temizler.
        var expectedEventId = order.DomainEvents.OfType<OrderConfirmed>().Single().EventId;

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // OrderCreated registry'de YOK + OrderConfirmed VAR → tam 1 satır.
        var messages = await context.OutboxMessages.ToListAsync();
        var message = messages.Should().ContainSingle().Which;
        message.Id.Should().Be(expectedEventId, "Id = kaynak EventId (uçtan uca dedup)");
        message.Type.Should().Be(typeof(ProcessPaymentIntegrationEvent).AssemblyQualifiedName);
        message.ProcessedOnUtc.Should().BeNull("henüz publish edilmedi (write-only)");
        message.RetryCount.Should().Be(0);

        var payload = JsonSerializer.Deserialize<ProcessPaymentIntegrationEvent>(message.Payload, PayloadOptions);
        payload.Should().NotBeNull();
        payload!.OrderId.Should().Be(order.Id);
        payload.CustomerId.Should().Be(order.CustomerId);
        payload.Amount.Should().Be(order.Total.Amount);
        payload.Currency.Should().Be(order.Total.Currency.ToString());

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

            // Aynı transaction içinde her ikisi de görünür (atomik yazıldı).
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
    public async Task SaveChanges_OrderCreatedUnmapped_WritesNoOutbox_ButInProcessDispatchStillRuns()
    {
        var publisherMock = LoosePublisher();

        await using var provider = BuildProvider(publisherMock.Object);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = OrderTestData.NewOrder(); // yalnızca OrderCreated (registry'de YOK).
        order.DomainEvents.OfType<OrderCreated>().Should().ContainSingle();

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // 1) Outbox boş: OrderCreated registry'de olmadığından hiçbir satır yazılmadı.
        (await context.OutboxMessages.CountAsync()).Should().Be(0);

        // 2) In-process dispatch ÇALIŞTI: DispatchDomainEventsInterceptor OrderCreated'ı publish etti (Faz 2 zinciri sağ).
        publisherMock.Verify(
            p => p.Publish(
                It.Is<INotification>(n => (n as DomainEventNotification<OrderCreated>) != null),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Faz 2 in-process dispatch kırılmamalı");

        // 3) Domain event'ler temizlendi → pre-commit outbox DEĞİL, post-commit dispatcher clear etti.
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
