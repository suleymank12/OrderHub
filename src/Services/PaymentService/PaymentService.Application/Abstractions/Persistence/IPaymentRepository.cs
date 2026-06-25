using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.Application.Abstractions.Persistence;

/// <summary>
/// <see cref="Payment"/> aggregate'i için persistence portu. Application'da tanımlı, Infrastructure EF Core
/// ile implement eder (DIP). Yalnızca persistence; iş kuralı içermez ve <c>IQueryable</c> sızdırmaz (§9).
/// </summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Aggregate'i takibe ekler (Id client-side üretilir → sync). Kalıcılaştırma
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> ile (TransactionBehavior) yapılır.
    /// </summary>
    void Add(Payment payment);

    /// <summary>Id ile ödemeyi yükler; bulunamazsa <c>null</c> döner.</summary>
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
