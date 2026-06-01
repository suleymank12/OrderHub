using OrderHub.OrderService.Infrastructure.Persistence.Repositories;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="OrderRepository"/>'nin gerçek SQL Server'a karşı davranışları: Add + SaveChanges
/// persistence, GetById'in kalemleri include etmesi, bulunamayan kayıtta null, sayfalama (count + page
/// + sıralama). Her test kendi transaction'ında izole (rollback).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrderRepositoryTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Add_AfterSaveChanges_PersistsOrder()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder();

        repository.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persisted = await context.Orders.FindAsync(order.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingOrder_ReturnsOrderWithItems()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);
        var order = OrderTestData.NewOrder();
        repository.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await repository.GetByIdAsync(order.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentOrder_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);

        var result = await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsRequestedPageSizeAndTotalCount()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);
        for (var i = 0; i < 5; i++)
        {
            repository.Add(OrderTestData.NewOrder());
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var (items, totalCount) = await repository.GetPagedAsync(page: 1, pageSize: 2, CancellationToken.None);

        totalCount.Should().Be(5);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_LastPage_ReturnsRemainingItems()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);
        for (var i = 0; i < 5; i++)
        {
            repository.Add(OrderTestData.NewOrder());
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var (items, _) = await repository.GetPagedAsync(page: 3, pageSize: 2, CancellationToken.None);

        items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPagedAsync_OrdersByCreatedAtDescending()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new OrderRepository(context);
        for (var i = 0; i < 3; i++)
        {
            repository.Add(OrderTestData.NewOrder());
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var (items, _) = await repository.GetPagedAsync(page: 1, pageSize: 10, CancellationToken.None);

        items.Should().BeInDescendingOrder(order => order.CreatedAtUtc);
    }
}
