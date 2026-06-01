using Microsoft.EntityFrameworkCore;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IPaymentRepository"/>'nin EF Core implementasyonu. Yalnızca persistence; iş kuralı içermez.
/// <c>IQueryable</c> dışarı sızdırmaz — tüm sorgular burada materialize edilir.
/// </summary>
internal sealed class PaymentRepository(PaymentDbContext context) : IPaymentRepository
{
    // Tracking'e ekler; kalıcılaştırma IUnitOfWork.SaveChangesAsync ile (TransactionBehavior).
    public void Add(Payment payment) => context.Payments.Add(payment);

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);
}
