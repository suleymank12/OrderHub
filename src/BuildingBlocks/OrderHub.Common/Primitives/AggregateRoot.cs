namespace OrderHub.Common.Primitives;

/// <summary>
/// Aggregate root'ların soyut tabanı: bir <see cref="Entity{TId}"/> olmasının yanında
/// domain olaylarını toplar. Olaylar yalnızca aggregate'in kendi behavior method'larından
/// <see cref="RaiseDomainEvent"/> ile yükseltilir, dışarı salt-okunur expose edilir.
/// Yayınlandıktan sonra Infrastructure (DbContext/Outbox) <see cref="ClearDomainEvents"/>
/// çağırır. Eşitlik <see cref="Entity{TId}"/>'den miras alınır (kimlik bazlı).
/// </summary>
/// <typeparam name="TId">Kimlik tipi (null olamaz).</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>EF Core materialization için.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>Kimliği ctor üzerinden atayan kuruluş.</summary>
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Henüz yayınlanmamış domain olayları (salt-okunur).</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Yayın sonrası olay listesini temizler (Infrastructure çağırır).</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Aggregate'in kendi behavior'larından yeni bir domain olayı yükseltir.</summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
