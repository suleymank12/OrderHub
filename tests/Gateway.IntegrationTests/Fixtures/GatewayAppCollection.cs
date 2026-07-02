namespace OrderHub.Gateway.IntegrationTests.Fixtures;

/// <summary>Gateway testleri için tek <see cref="GatewayAppFixture"/>'ı (host + WireMock downstream) paylaşan collection.</summary>
[CollectionDefinition(Name)]
public sealed class GatewayAppCollection : ICollectionFixture<GatewayAppFixture>
{
    public const string Name = "GatewayApp";
}
