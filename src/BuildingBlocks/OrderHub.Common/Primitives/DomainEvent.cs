namespace OrderHub.Common.Primitives;

/// <summary>
/// <see cref="IDomainEvent"/> için ortak taban. Somut olaylar bunu miras alır:
/// <c>public sealed record OrderCreated(Guid OrderId) : DomainEvent;</c>. Kimlik ve
/// zaman damgası varsayılan olarak üretim anında atanır.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
