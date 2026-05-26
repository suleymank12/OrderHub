using OrderHub.Common.Primitives;

namespace OrderHub.Common.UnitTests.Primitives;

public sealed class DomainEventTests
{
    [Fact]
    public void EventId_DefaultsToNonEmptyGuid()
    {
        new TestDomainEvent().EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void OccurredOnUtc_DefaultsToCurrentUtcTime()
    {
        var domainEvent = new TestDomainEvent();

        domainEvent.OccurredOnUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        domainEvent.OccurredOnUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void TwoInstances_HaveDifferentEventIds()
    {
        var first = new TestDomainEvent();
        var second = new TestDomainEvent();

        first.EventId.Should().NotBe(second.EventId);
    }

    [Fact]
    public void EventId_CanBeOverriddenViaInit()
    {
        var knownId = Guid.NewGuid();

        var domainEvent = new TestDomainEvent { EventId = knownId };

        domainEvent.EventId.Should().Be(knownId);
    }
}

/// <summary>Soyut <see cref="DomainEvent"/>'i test etmek için minimal somut olay.</summary>
file sealed record TestDomainEvent : DomainEvent;
