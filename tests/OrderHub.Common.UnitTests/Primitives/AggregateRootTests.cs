using OrderHub.Common.Primitives;

namespace OrderHub.Common.UnitTests.Primitives;

public sealed class AggregateRootTests
{
    [Fact]
    public void NewAggregate_HasNoDomainEvents()
    {
        new TestAggregate(Guid.NewGuid()).DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ParameterlessConstructor_StartsWithNoDomainEvents()
    {
        // EF Core materialization senaryosu: DB'den yüklenen aggregate stale event taşımamalı.
        new TestAggregate().DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RaiseDomainEvent_AddsEventToCollection()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        var domainEvent = new TestEvent();

        aggregate.Raise(domainEvent);

        aggregate.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(domainEvent);
    }

    [Fact]
    public void RaiseDomainEvent_MultipleEvents_PreservesInsertionOrder()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        var first = new TestEvent();
        var second = new TestEvent();

        aggregate.Raise(first);
        aggregate.Raise(second);

        aggregate.DomainEvents.Should().ContainInOrder(first, second);
    }

    [Fact]
    public void RaiseDomainEvent_Null_ThrowsArgumentNullException()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        var act = () => aggregate.Raise(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.Raise(new TestEvent());

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DomainEvents_CannotBeDownCastToMutableList()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.Raise(new TestEvent());

        (aggregate.DomainEvents as List<IDomainEvent>).Should().BeNull();
    }

    [Fact]
    public void Equals_SameId_UsesInheritedIdentityEquality()
    {
        var id = Guid.NewGuid();

        new TestAggregate(id).Equals(new TestAggregate(id)).Should().BeTrue();
    }
}

file sealed class TestAggregate : AggregateRoot<Guid>
{
    public TestAggregate()
    {
    }

    public TestAggregate(Guid id)
        : base(id)
    {
    }

    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

/// <summary>Domain event collection davranışını test etmek için minimal olay.</summary>
file sealed record TestEvent : DomainEvent;
