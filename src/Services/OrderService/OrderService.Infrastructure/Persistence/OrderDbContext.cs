using Microsoft.EntityFrameworkCore;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Domain.Orders;

namespace OrderHub.OrderService.Infrastructure.Persistence;

/// <summary>
/// OrderService'in EF Core <see cref="DbContext"/>'i ve <see cref="IUnitOfWork"/> implementasyonu.
/// <see cref="IUnitOfWork.SaveChangesAsync"/> doğrudan <see cref="DbContext"/>'in inherited metodu ile
/// karşılanır → repository ile aynı scope/instance paylaşıldığında biriken değişiklikler atomik kalıcılaşır.
/// Sadece <see cref="Order"/> aggregate root'u DbSet olarak expose edilir; kalemlere yalnızca aggregate
/// üzerinden erişilir (aggregate boundary).
/// </summary>
internal sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tüm IEntityTypeConfiguration'lar (Order, OrderItem) bu assembly'den uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
