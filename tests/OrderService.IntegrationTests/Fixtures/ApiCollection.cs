namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Api integration test'leri için collection: <see cref="ApiTestFactory"/> session başına bir kez kurulur
/// (tek container/host). Test'ler seri çalışır → her test <c>ResetDatabaseAsync</c> ile izole olur.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiTestFactory>
{
    public const string Name = "Api";
}
