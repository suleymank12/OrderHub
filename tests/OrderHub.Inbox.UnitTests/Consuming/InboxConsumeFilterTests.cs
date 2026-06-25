using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.EventBus;
using OrderHub.Inbox.Consuming;
using OrderHub.Inbox.Persistence;

namespace OrderHub.Inbox.UnitTests.Consuming;

/// <summary>
/// <see cref="InboxConsumeFilter{TMessage}"/> dedup mantığı (ADR-0005, message-level). Hafif InMemory
/// <see cref="IInboxDbContext"/> ile: duplicate → consumer (next) çağrılmaz; yeni → InboxMessage tracked
/// Add + next çağrılır (SaveChanges consumer'ın TransactionBehavior'ında — burada ChangeTracker ile doğrulanır).
/// </summary>
public sealed class InboxConsumeFilterTests
{
    private static readonly string MessageTypeName = typeof(TestIntegrationEvent).FullName!;

    [Fact]
    public async Task Send_AlreadyProcessedMessage_DoesNotCallNext()
    {
        await using var dbContext = NewInMemoryDbContext();
        var messageId = Guid.NewGuid();
        dbContext.InboxMessages.Add(InboxMessage.Create(messageId, MessageTypeName));
        await dbContext.SaveChangesAsync();

        var next = new Mock<IPipe<ConsumeContext<TestIntegrationEvent>>>();
        var filter = NewFilter(dbContext);

        await filter.Send(ConsumeContext(messageId), next.Object);

        // Dedup: consumer pipeline'ı hiç çalışmaz.
        next.Verify(pipe => pipe.Send(It.IsAny<ConsumeContext<TestIntegrationEvent>>()), Times.Never);
    }

    [Fact]
    public async Task Send_NewMessage_AddsInboxRecordAndCallsNext()
    {
        await using var dbContext = NewInMemoryDbContext();
        var messageId = Guid.NewGuid();

        var next = new Mock<IPipe<ConsumeContext<TestIntegrationEvent>>>();
        next.Setup(pipe => pipe.Send(It.IsAny<ConsumeContext<TestIntegrationEvent>>()))
            .Returns(Task.CompletedTask);
        var filter = NewFilter(dbContext);

        var context = ConsumeContext(messageId);
        await filter.Send(context, next.Object);

        // Consumer pipeline'ı çağrıldı.
        next.Verify(pipe => pipe.Send(context), Times.Once);

        // InboxMessage tracked eklendi (henüz SaveChanges YOK → Added state; commit handler'ın tek
        // SaveChanges'inde inbox + iş ile atomik olur).
        dbContext.ChangeTracker.Entries<InboxMessage>().Should().ContainSingle(entry =>
            entry.State == EntityState.Added &&
            entry.Entity.MessageId == messageId &&
            entry.Entity.MessageType == MessageTypeName);
    }

    private static InboxConsumeFilter<TestIntegrationEvent> NewFilter(IInboxDbContext dbContext) =>
        new(dbContext, NullLogger<InboxConsumeFilter<TestIntegrationEvent>>.Instance);

    private static ConsumeContext<TestIntegrationEvent> ConsumeContext(Guid messageId)
    {
        var mock = new Mock<ConsumeContext<TestIntegrationEvent>>();
        mock.Setup(context => context.Message)
            .Returns(new TestIntegrationEvent { Id = messageId, OccurredOnUtc = DateTime.UtcNow });
        mock.Setup(context => context.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    private static TestInboxDbContext NewInMemoryDbContext() =>
        new(new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase($"inbox-{Guid.NewGuid()}")
            .Options);
}

/// <summary>Test integration event'i — filter dedup anahtarını <see cref="IIntegrationEvent.Id"/>'den alır.
/// Moq generic mock proxy'si erişebilsin diye public.</summary>
public sealed record TestIntegrationEvent : IIntegrationEvent
{
    public required Guid Id { get; init; }

    public required DateTime OccurredOnUtc { get; init; }
}

/// <summary>Filter testi için hafif InMemory <see cref="IInboxDbContext"/> (flat InboxMessages tablosu).</summary>
internal sealed class TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
    : DbContext(options), IInboxDbContext
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
}
