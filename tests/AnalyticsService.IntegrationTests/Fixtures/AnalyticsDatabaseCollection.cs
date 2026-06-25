namespace OrderHub.AnalyticsService.IntegrationTests.Fixtures;

/// <summary>SQL container'ı session başına tek paylaşan collection (hız) — DbContext/projection testleri.</summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsDatabaseCollection : ICollectionFixture<AnalyticsSqlServerContainerFixture>
{
    public const string Name = "AnalyticsDatabase";
}
