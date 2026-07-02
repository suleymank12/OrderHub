using Microsoft.EntityFrameworkCore;
using OrderHub.OrderProcessingService.Infrastructure.Saga;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence;

/// <summary>
/// OrderProcessingService'in EF Core DbContext'i — yalnız MassTransit saga state tablosunu barındırır
/// (<c>OrderHub_Sagas</c>, database-per-service). Outbox/Inbox tablosu YOK: saga, üretilen mesajları
/// <c>UseInMemoryOutbox</c> ile state commit'ine kadar tamponlar (Karar B/D), redelivery idempotency'si
/// ProductId kümeleriyle sağlanır → ayrı inbox tablosuna gerek kalmaz.
/// </summary>
internal sealed class SagasDbContext(DbContextOptions<SagasDbContext> options) : DbContext(options)
{
    public DbSet<OrderProcessingSagaState> SagaStates => Set<OrderProcessingSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bu assembly'deki IEntityTypeConfiguration'lar (OrderProcessingSagaStateConfiguration) otomatik uygulanır.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SagasDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
