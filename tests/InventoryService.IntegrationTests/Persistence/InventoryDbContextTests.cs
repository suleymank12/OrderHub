using Microsoft.EntityFrameworkCore;
using OrderHub.InventoryService.Domain.Catalog;
using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.IntegrationTests.Fixtures;

namespace OrderHub.InventoryService.IntegrationTests.Persistence;

/// <summary>
/// Faz 5 5b — <b>gerçek SQL Server</b> ile InventoryDbContext şema/eşleme doğrulaması. Risk:
/// StockItem→Reservations backing-field one-to-many (salt-okunur nav) + shadow FK + RowVersion. Migration
/// (<c>OrderHub_Inventory</c>) uygulanıp aggregate round-trip edilir.
/// </summary>
public sealed class InventoryDbContextTests(InventorySqlServerContainerFixture fixture)
    : IClassFixture<InventorySqlServerContainerFixture>
{
    [Fact]
    public async Task StockItemWithReservation_RoundTrips_WithBackingFieldNavigation()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        Guid stockItemId;

        await using (var context = fixture.CreateContext())
        {
            var item = StockItem.Create(productId, 10);
            item.Reserve(orderId, 3, DateTime.UtcNow.AddMinutes(15));
            context.StockItems.Add(item);
            await context.SaveChangesAsync();
            stockItemId = item.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var reloaded = await context.StockItems
                .Include(stockItem => stockItem.Reservations)
                .AsNoTracking()
                .FirstAsync(stockItem => stockItem.Id == stockItemId);

            reloaded.AvailableQuantity.Should().Be(7);
            reloaded.ProductId.Should().Be(productId);
            reloaded.Reservations.Should().ContainSingle(reservation =>
                reservation.OrderId == orderId
                && reservation.Quantity == 3
                && reservation.Status == ReservationStatus.Pending);
        }
    }

    [Fact]
    public async Task ConfirmReservation_PersistsStatusChange()
    {
        var orderId = Guid.NewGuid();
        Guid stockItemId;

        await using (var context = fixture.CreateContext())
        {
            var item = StockItem.Create(Guid.NewGuid(), 5);
            item.Reserve(orderId, 2, DateTime.UtcNow.AddMinutes(15));
            context.StockItems.Add(item);
            await context.SaveChangesAsync();
            stockItemId = item.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var item = await context.StockItems
                .Include(stockItem => stockItem.Reservations)
                .FirstAsync(stockItem => stockItem.Id == stockItemId);
            item.ConfirmReservation(orderId);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var reloaded = await context.StockItems
                .Include(stockItem => stockItem.Reservations)
                .AsNoTracking()
                .FirstAsync(stockItem => stockItem.Id == stockItemId);

            reloaded.Reservations.Single().Status.Should().Be(ReservationStatus.Confirmed);
            reloaded.AvailableQuantity.Should().Be(3, "confirm available'ı değiştirmez (reserve'de düştü)");
        }
    }

    [Fact]
    public async Task Product_RoundTrips()
    {
        var product = Product.Create("Widget", "SKU-001");

        await using (var context = fixture.CreateContext())
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
            reloaded.Name.Should().Be("Widget");
            reloaded.Sku.Should().Be("SKU-001");
        }
    }
}
