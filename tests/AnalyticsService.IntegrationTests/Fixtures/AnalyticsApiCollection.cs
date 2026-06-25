namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>
/// Read-only Api testleri için collection: <see cref="AnalyticsApiTestFactory"/>'yi (SQL container + in-process
/// host) session başına tek paylaşır. Consumer/DbContext testlerinin collection'larından ayrıdır → gereksiz
/// host spin-up'ı izole tutulur.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsApiCollection : ICollectionFixture<AnalyticsApiTestFactory>
{
    public const string Name = "AnalyticsApi";
}
