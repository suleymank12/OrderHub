using OrderHub.OrderService.Infrastructure.Persistence.Repositories;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="OrderRepository.GetPendingOlderThanAsync"/> gerçek SQL Server'a karşı davranış testleri.
/// <para>
/// Strateji: cutoff değerini oynatarak test ederiz — <c>CreatedAtUtc</c>'yi doğrudan manipüle
/// ETMEYIZ (domain property private setter; RowVersion concurrency bozulur).
/// <c>cutoff = UtcNow + 1dk</c> → sipariş dönmeli; <c>cutoff = UtcNow - 1dk</c> → dönmemeli.
/// </para>
/// Her test kendi transaction'ında izole (rollback): DB'de kirli veri bırakmaz.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetPendingOlderThanRepositoryTests(SqlServerContainerFixture fixture)
{
    // -------------------------------------------------------------------------
    // Senaryo 1: Pending sipariş cutoff'tan önce oluşturulmuş → sorgu döner
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingOlderThanAsync_PendingOrderAndCutoffInFuture_ReturnsOrder()
    {
        // Arrange — cutoff = UtcNow + 1dk → yeni oluşturulan sipariş "cutoff'tan eski" görünür.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder(); // CreatedAtUtc ≈ UtcNow
        repository.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var cutoffInFuture = DateTime.UtcNow.AddMinutes(1);

        // Act
        var result = await repository.GetPendingOlderThanAsync(cutoffInFuture, CancellationToken.None);

        // Assert
        result.Should().ContainSingle(o => o.Id == order.Id,
            "Pending sipariş cutoff'tan önce oluşturulduğunda sweep tarafından yakalanmalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Pending sipariş cutoff'tan sonra oluşturulmuş → sorgu döndürmez
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingOlderThanAsync_PendingOrderAndCutoffInPast_ReturnsEmpty()
    {
        // Arrange — cutoff = UtcNow - 1dk → sipariş "cutoff'tan yeni" → filtreye girmez.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder();
        repository.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var cutoffInPast = DateTime.UtcNow.AddMinutes(-1);

        // Act
        var result = await repository.GetPendingOlderThanAsync(cutoffInPast, CancellationToken.None);

        // Assert — filtreye girmiyor (yeni sipariş henüz zaman aşımına uğramamış).
        result.Should().NotContain(o => o.Id == order.Id,
            "Pending sipariş cutoff'tan sonra oluşturulduğunda sweep tarafından yakalanmamalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: İptal edilmiş sipariş → Status filtresi; hiçbir cutoff'ta dönmemeli
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingOlderThanAsync_CancelledOrder_NeverReturned()
    {
        // Arrange — sipariş oluştur → iptal et → kaydet.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder();
        repository.Add(order);
        await context.SaveChangesAsync();

        // İptal et ve güncelle.
        order.Cancel("sweep-test-cancel");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // cutoff'u geniş tut: siparişin oluşturma zamanından kesinlikle sonra.
        var wideCutoff = DateTime.UtcNow.AddMinutes(10);

        // Act
        var result = await repository.GetPendingOlderThanAsync(wideCutoff, CancellationToken.None);

        // Assert — Status=Cancelled → Pending filtresi dışlar.
        result.Should().NotContain(o => o.Id == order.Id,
            "İptal edilmiş sipariş artık Pending değil; Status filtresi dışlamalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 4: Tracked döner (SaveChanges ile kalıcılaştırılabilir)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingOlderThanAsync_ReturnsTrackedOrders_CanBeCancelledAndSaved()
    {
        // Arrange
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder();
        repository.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var cutoffInFuture = DateTime.UtcNow.AddMinutes(1);

        // Act — al, iptal et, kaydet.
        var staleOrders = await repository.GetPendingOlderThanAsync(cutoffInFuture, CancellationToken.None);
        foreach (var stale in staleOrders)
        {
            stale.Cancel("payment_timeout");
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Assert — DB'de gerçekten Cancelled oldu (tracking açık, change detection çalışıyor).
        var persisted = await context.Orders.FindAsync(order.Id);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(OrderHub.OrderService.Domain.Orders.OrderStatus.Cancelled,
            "GetPendingOlderThanAsync tracked döndürmeli; Cancel + SaveChanges ile kalıcılaşmalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 5: Birden fazla Pending sipariş — hepsi döner
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingOlderThanAsync_MultiplePendingOrders_ReturnsAll()
    {
        // Arrange
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var repository = new OrderRepository(context);
        var ids = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            var order = OrderTestData.NewOrder();
            ids.Add(order.Id);
            repository.Add(order);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var cutoffInFuture = DateTime.UtcNow.AddMinutes(1);

        // Act
        var result = await repository.GetPendingOlderThanAsync(cutoffInFuture, CancellationToken.None);

        // Assert — eklenen 3 sipariş de sorgu sonucunda olmalı.
        result.Select(o => o.Id).Should().Contain(ids,
            "Tüm Pending siparişler cutoff filtresiyle eşleştiğinde döndürülmelidir");
    }
}
