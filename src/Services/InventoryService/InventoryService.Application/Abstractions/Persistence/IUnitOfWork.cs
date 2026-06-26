namespace OrderHub.InventoryService.Application.Abstractions.Persistence;

/// <summary>
/// Bir command boyunca biriken değişiklikleri atomik olarak kalıcılaştıran iş birimi portu.
/// Implementasyon EF Core <c>DbContext</c>'tir; tek <see cref="SaveChangesAsync"/> çağrısı EF tarafında
/// tek transaction'a sarılır. Outbox satırı da aynı transaction'da yazılır (pre-commit interceptor).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Birikmiş değişiklikleri kalıcılaştırır; etkilenen satır sayısını döner.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
