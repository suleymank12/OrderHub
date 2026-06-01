namespace OrderHub.Common.Primitives;

/// <summary>
/// Kimliğe (Id) sahip domain entity'lerinin soyut tabanı. Eşitlik <b>kimlik bazlıdır</b>:
/// aynı runtime tip + aynı (transient olmayan) <see cref="Id"/> ⇒ eşit. Generic
/// <typeparamref name="TId"/> ileride strongly-typed ID'lere kapıyı açık tutar
/// (pratikte <see cref="System.Guid"/> kullanılır). Lazy-loading proxy kullanılmadığı
/// için <c>GetType()</c> karşılaştırması güvenlidir.
/// </summary>
/// <typeparam name="TId">Kimlik tipi (null olamaz).</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>EF Core materialization ve türev factory'lerin object-initializer'ı için.</summary>
    protected Entity()
    {
    }

    /// <summary>Kimliği ctor üzerinden atayan kuruluş.</summary>
    protected Entity(TId id) => Id = id;

    /// <summary>Entity kimliği. Yalnızca türev/EF set edebilir.</summary>
    public TId Id { get; protected set; } = default!;

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        // Transient (henüz kimlik atanmamış) entity'ler yalnızca referans-eşittir.
        if (IsTransient() || other.IsTransient())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Kimlik bazlı eşitlik operatörü.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Kimlik bazlı eşitsizlik operatörü.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    private bool IsTransient() => EqualityComparer<TId>.Default.Equals(Id, default!);
}
