using Microsoft.EntityFrameworkCore;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="OrderHub.OrderService.Infrastructure.Persistence.OrderDbContext"/>'in gerçek SQL Server'a
/// karşı eşleme davranışları: owned type round-trip, UTC kind koruması, RowVersion shadow token, cascade
/// delete ve OrderItem private-ctor materialization. Her test kendi transaction'ında izole (rollback).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrderDbContextTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task SaveAndReload_OrderWithOwnedTypes_RoundTripsAllValues()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear(); // DB'den taze materialization'ı zorla (tracking cache değil).

        var reloaded = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);

        reloaded.CustomerId.Should().Be(order.CustomerId);
        reloaded.Status.Should().Be(OrderStatus.Pending);
        reloaded.Total.Amount.Should().Be(100m);
        reloaded.Total.Currency.Should().Be(Currency.TRY);
        reloaded.ShippingAddress.City.Should().Be("Istanbul");
        reloaded.ShippingAddress.Country.Should().Be("Türkiye");
        reloaded.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Reload_OrderItem_MaterializesViaPrivateCtorWithOwnedMoney()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);

        var item = reloaded.Items.Single();
        item.Quantity.Should().Be(2);
        item.UnitPrice.Amount.Should().Be(50m);
        item.UnitPrice.Currency.Should().Be(Currency.TRY);
        item.Subtotal.Amount.Should().Be(100m); // computed property materialization sonrası çalışıyor.
    }

    [Fact]
    public async Task Reload_DateTimeColumns_PreserveUtcKind()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();
        order.Confirm(); // ConfirmedAtUtc dolsun.
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.Orders.SingleAsync(o => o.Id == order.Id);

        reloaded.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        reloaded.ConfirmedAtUtc.Should().NotBeNull();
        reloaded.ConfirmedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Save_NewOrder_AssignsRowVersionShadowToken()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var rowVersion = context.Entry(order).Property<byte[]>("RowVersion").CurrentValue;
        rowVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveChanges_ClearsAggregateDomainEventsPostCommit()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();
        order.DomainEvents.Should().NotBeEmpty(); // Order.Create OrderCreated yükseltti.

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        order.DomainEvents.Should().BeEmpty(); // ClearDomainEventsInterceptor commit sonrası temizledi.
    }

    [Fact]
    public async Task RemoveOrder_CascadeDeletesItems()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var order = OrderTestData.NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        context.Orders.Remove(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var remainingItems = await context.Set<OrderItem>()
            .CountAsync(item => EF.Property<Guid>(item, "OrderId") == order.Id);
        remainingItems.Should().Be(0);
    }
}
