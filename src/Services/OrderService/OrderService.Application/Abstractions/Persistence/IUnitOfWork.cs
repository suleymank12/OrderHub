namespace OrderHub.OrderService.Application.Abstractions.Persistence;

/// <summary>
/// Bir command boyunca biriken değişiklikleri atomik olarak kalıcılaştıran iş birimi portu.
/// Implementasyon (Faz 1.4) EF Core <c>DbContext</c>'tir. Tek bir <see cref="SaveChangesAsync"/>
/// çağrısı EF tarafında zaten tek bir transaction'a sarılır → Faz 1.3'te explicit
/// <c>BeginTransaction</c> gereksizdir. Çoklu-write/Outbox geldiğinde (Faz 3) bu port'a explicit
/// transaction metodları eklenir; o ana kadar bu komiti <c>TransactionBehavior</c> yönetir.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Birikmiş değişiklikleri kalıcılaştırır; etkilenen satır sayısını döner.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
