using Microsoft.EntityFrameworkCore;
using OrderHub.OrderProcessingService.Infrastructure.Saga;
using OrderHub.OrderProcessingService.IntegrationTests.Fixtures;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga;

/// <summary>
/// Faz 5 5d-7 <b>Test A</b> — saga state RowVersion optimistic locking'inin <b>izole, deterministik</b> kanıtı
/// (gerçek SQL Server; <c>rowversion</c> provider'a özgü). İki <c>SagasDbContext</c> aynı saga satırını AYNI
/// RowVersion token'ıyla yükler; biri kaydeder (token artar), diğeri bayat token'la kaydetmeye çalışır →
/// <see cref="DbUpdateConcurrencyException"/>.
/// <para>
/// ★ <b>Dürüst sınır (bu testin kanıtladığı ve kanıtlamadığı):</b> Bu test, MassTransit'in
/// <c>ConcurrencyMode.Optimistic</c> saga repository'sinin dayandığı RowVersion token'ının gerçek şemada
/// KURULU ve ÇALIŞIR olduğunu kanıtlar. MassTransit'in çakışma sonrası <b>retry döngüsünü</b> (mesajı yeniden
/// çalıştırıp taze state ile devam) kanıtlamaz — o framework davranışıdır; bizim sorumluluğumuz config'in doğru
/// token'ı sağladığıdır ve tam olarak bu doğrulanır. Gerçek altyapıda uçtan uca akış Test B'dedir
/// (o da contention DEĞİL, final tutarlılık kanıtıdır).
/// </para>
/// </summary>
[Collection(SagasDatabaseCollection.Name)]
public sealed class RowVersionConcurrencyTests(SagasSqlServerContainerFixture fixture)
{
    [Fact]
    public async Task ConcurrentWritesToSameSagaRow_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        // Seed: AwaitingStockReservation, henüz rezervasyon yok (iki fan-in olayının yarışacağı başlangıç).
        await using (var seed = fixture.CreateContext())
        {
            seed.SagaStates.Add(new OrderProcessingSagaState
            {
                CorrelationId = orderId,
                CurrentState = "AwaitingStockReservation",
                CustomerId = Guid.NewGuid(),
                Amount = 50m,
                Currency = "TRY",
                ItemCount = 2,
                AllProductIds = [productA, productB],
                ReservedProductIds = [],
                ConfirmedProductIds = [],
            });
            await seed.SaveChangesAsync();
        }

        // İki bağımsız context AYNI satırı, AYNI RowVersion token'ıyla yükler (eşzamanlı fan-in simülasyonu).
        await using var context1 = fixture.CreateContext();
        await using var context2 = fixture.CreateContext();

        var reservedByFirst = await context1.SagaStates.FirstAsync(saga => saga.CorrelationId == orderId);
        var reservedBySecond = await context2.SagaStates.FirstAsync(saga => saga.CorrelationId == orderId);

        // 1. yazar başarılı → DB'deki RowVersion artar; context2'nin okuduğu token artık BAYAT.
        reservedByFirst.ReservedProductIds.Add(productA);
        await context1.SaveChangesAsync();

        // 2. yazar bayat token'la UPDATE dener → WHERE RowVersion=@stale 0 satır etkiler → optimistic çakışma.
        reservedBySecond.ReservedProductIds.Add(productB);
        var secondSave = async () => await context2.SaveChangesAsync();

        await secondSave.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "bayat RowVersion token'ı EF optimistic concurrency kontrolünce reddedilir — bu, MassTransit'in " +
            "ConcurrencyMode.Optimistic saga repo'sunun eşzamanlı fan-in mesajlarını serileştirmek için " +
            "güvendiği token'ın ta kendisidir");
    }
}
