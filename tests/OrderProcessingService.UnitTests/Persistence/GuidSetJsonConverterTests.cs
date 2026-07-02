using OrderHub.OrderProcessingService.Infrastructure.Persistence.Converters;

namespace OrderHub.OrderProcessingService.UnitTests.Persistence;

/// <summary>
/// <see cref="GuidSetJsonConverter"/> round-trip testleri: saga fan-out kümeleri (Karar B) JSON kolona yazılıp
/// geri okununca içerik korunmalı; boş/null girdi boş kümeye düşmeli (saga ilk persist'te kümeler boş olabilir).
/// </summary>
public sealed class GuidSetJsonConverterTests
{
    private readonly Func<HashSet<Guid>, string> _toProvider;
    private readonly Func<string, HashSet<Guid>> _fromProvider;

    public GuidSetJsonConverterTests()
    {
        var converter = new GuidSetJsonConverter();
        _toProvider = converter.ConvertToProviderExpression.Compile();
        _fromProvider = converter.ConvertFromProviderExpression.Compile();
    }

    [Fact]
    public void RoundTrip_PopulatedSet_PreservesAllGuids()
    {
        var original = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var json = _toProvider(original);
        var restored = _fromProvider(json);

        restored.Should().BeEquivalentTo(original);
        restored.SetEquals(original).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_EmptySet_StaysEmpty()
    {
        var json = _toProvider([]);
        var restored = _fromProvider(json);

        json.Should().Be("[]");
        restored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromProvider_NullOrWhitespace_ReturnsEmptySet(string json)
    {
        var restored = _fromProvider(json);

        restored.Should().NotBeNull();
        restored.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_DuplicateAwareness_SetSemanticsHold()
    {
        var id = Guid.NewGuid();
        var original = new HashSet<Guid> { id };

        // Aynı id'yi tekrar eklemek küme'yi değiştirmez (redelivery idempotency'sinin temeli, Karar B).
        original.Add(id);
        var restored = _fromProvider(_toProvider(original));

        restored.Should().ContainSingle().Which.Should().Be(id);
    }
}
