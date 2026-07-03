namespace OrderHub.Gateway.IntegrationTests.Fixtures;

/// <summary>Resilience testleri için tek <see cref="ResilienceAppFixture"/>'ı paylaşan collection (deterministik CB config + kendi WireMock).</summary>
[CollectionDefinition(Name)]
public sealed class ResilienceAppCollection : ICollectionFixture<ResilienceAppFixture>
{
    public const string Name = "ResilienceApp";
}
