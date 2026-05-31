namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection tanımı: <see cref="SqlServerContainerFixture"/> session başına bir kez kurulur ve
/// koleksiyondaki tüm test sınıflarınca paylaşılır (her sınıf için yeni container = yavaş + savurgan).
/// Koleksiyon test'leri seri çalışır → her test kendi transaction'ında izole olur (rollback).
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "Database";
}
