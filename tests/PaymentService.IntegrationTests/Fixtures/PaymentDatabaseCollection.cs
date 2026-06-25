namespace OrderHub.PaymentService.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection tanımı: <see cref="PaymentSqlServerContainerFixture"/> session başına bir kez kurulur,
/// koleksiyondaki testlerce paylaşılır. Koleksiyon test'leri seri çalışır (assembly genelinde paralelizasyon
/// kapalı, aşağıdaki AssemblyInfo).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PaymentDatabaseCollection : ICollectionFixture<PaymentSqlServerContainerFixture>
{
    public const string Name = "PaymentDatabase";
}
