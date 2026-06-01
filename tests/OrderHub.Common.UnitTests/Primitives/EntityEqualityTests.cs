using OrderHub.Common.Primitives;

namespace OrderHub.Common.UnitTests.Primitives;

public sealed class EntityEqualityTests
{
    [Fact]
    public void Equals_SameTypeSameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();

        new TestEntity(id).Equals(new TestEntity(id)).Should().BeTrue();
    }

    [Fact]
    public void Equals_SameTypeDifferentId_ReturnsFalse()
    {
        new TestEntity(Guid.NewGuid()).Equals(new TestEntity(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentTypeSameId_ReturnsFalse()
    {
        var id = Guid.NewGuid();

        new TestEntity(id).Equals(new DifferentEntity(id)).Should().BeFalse();
    }

    [Fact]
    public void Equals_BothTransient_ReturnsFalse()
    {
        new TestEntity().Equals(new TestEntity()).Should().BeFalse();
    }

    [Fact]
    public void Equals_PersistedVersusTransient_ReturnsFalse()
    {
        // Kalıcı (Id atanmış) entity, henüz kaydedilmemiş transient entity'ye eşit değildir.
        var persisted = new TestEntity(Guid.NewGuid());
        var transient = new TestEntity();

        persisted.Equals(transient).Should().BeFalse();
    }

    [Fact]
    public void Equals_NonEntityObject_ReturnsFalse()
    {
        // object overload'u: yabancı tip ile karşılaştırma false döner (fırlatmaz).
        object notAnEntity = "not an entity";

        new TestEntity(Guid.NewGuid()).Equals(notAnEntity).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        new TestEntity(Guid.NewGuid()).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_SameReference_ReturnsTrue()
    {
        var entity = new TestEntity(Guid.NewGuid());

        entity.Equals(entity).Should().BeTrue();
    }

    [Fact]
    public void OperatorEquals_EqualEntities_ReturnsTrue()
    {
        var id = Guid.NewGuid();

        (new TestEntity(id) == new TestEntity(id)).Should().BeTrue();
    }

    [Fact]
    public void OperatorEquals_BothNull_ReturnsTrue()
    {
        TestEntity? left = null;
        TestEntity? right = null;

        (left == right).Should().BeTrue();
    }

    [Fact]
    public void OperatorNotEquals_DifferentEntities_ReturnsTrue()
    {
        (new TestEntity(Guid.NewGuid()) != new TestEntity(Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_EqualEntities_ProduceSameHash()
    {
        var id = Guid.NewGuid();

        new TestEntity(id).GetHashCode().Should().Be(new TestEntity(id).GetHashCode());
    }
}

file sealed class TestEntity : Entity<Guid>
{
    public TestEntity()
    {
    }

    public TestEntity(Guid id)
        : base(id)
    {
    }
}

file sealed class DifferentEntity : Entity<Guid>
{
    public DifferentEntity(Guid id)
        : base(id)
    {
    }
}
