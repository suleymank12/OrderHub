using Microsoft.EntityFrameworkCore;
using OrderHub.OrderProcessingService.IntegrationTests.Fixtures;
using OrderHub.OrderProcessingService.Infrastructure.Saga;

namespace OrderHub.OrderProcessingService.IntegrationTests.Persistence;

/// <summary>
/// Faz 5 5d-4a — <b>gerçek SQL Server</b> ile <c>SagasDbContext</c> şema/eşleme doğrulaması. Risk: saga state'in
/// PK (= OrderId), <c>RowVersion</c> optimistic token ve fan-out kümelerinin (<c>AllProductIds</c>/
/// <c>ReservedProductIds</c>/<c>ConfirmedProductIds</c>) JSON kolon eşlemesi. Migration (<c>OrderHub_Sagas</c>)
/// uygulanıp saga state round-trip edilir. (State machine transition'ları 5d-4b — burada yalnız persistence.)
/// </summary>
[Collection(SagasDatabaseCollection.Name)]
public sealed class SagasDbContextTests(SagasSqlServerContainerFixture fixture)
{
    [Fact]
    public async Task SagaState_WithProductIdSets_RoundTripsAndPersistsRowVersion()
    {
        var orderId = Guid.NewGuid();
        var product1 = Guid.NewGuid();
        var product2 = Guid.NewGuid();

        await using (var context = fixture.CreateContext())
        {
            context.SagaStates.Add(new OrderProcessingSagaState
            {
                CorrelationId = orderId,
                CurrentState = "AwaitingStockReservation",
                CustomerId = Guid.NewGuid(),
                Amount = 149.90m,
                Currency = "TRY",
                ItemCount = 2,
                AllProductIds = [product1, product2],
                ReservedProductIds = [product1],
                ConfirmedProductIds = [],
            });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var reloaded = await context.SagaStates
                .AsNoTracking()
                .FirstAsync(state => state.CorrelationId == orderId);

            reloaded.CurrentState.Should().Be("AwaitingStockReservation");
            reloaded.Amount.Should().Be(149.90m);
            reloaded.Currency.Should().Be("TRY");
            reloaded.ItemCount.Should().Be(2);
            reloaded.AllProductIds.Should().BeEquivalentTo(new[] { product1, product2 });
            reloaded.ReservedProductIds.Should().BeEquivalentTo(new[] { product1 });
            reloaded.ConfirmedProductIds.Should().BeEmpty();
            reloaded.RowVersion.Should().NotBeNullOrEmpty("SQL Server rowversion insert'te üretilmeli (optimistic token)");
        }
    }

    [Fact]
    public async Task SagaState_MutatedProductIdSet_PersistsViaValueComparer()
    {
        var orderId = Guid.NewGuid();
        var product1 = Guid.NewGuid();
        var product2 = Guid.NewGuid();

        await using (var context = fixture.CreateContext())
        {
            context.SagaStates.Add(new OrderProcessingSagaState
            {
                CorrelationId = orderId,
                CurrentState = "AwaitingStockReservation",
                CustomerId = Guid.NewGuid(),
                Amount = 10m,
                Currency = "USD",
                ItemCount = 2,
                AllProductIds = [product1, product2],
                ReservedProductIds = [product1],
                ConfirmedProductIds = [],
            });
            await context.SaveChangesAsync();
        }

        // Küme'ye yeni ProductId eklenince ValueComparer mutasyonu yakalamalı (aksi halde EF değişikliği kaçırır).
        await using (var context = fixture.CreateContext())
        {
            var tracked = await context.SagaStates.FirstAsync(state => state.CorrelationId == orderId);
            tracked.ReservedProductIds.Add(product2);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var reloaded = await context.SagaStates.AsNoTracking()
                .FirstAsync(state => state.CorrelationId == orderId);

            reloaded.ReservedProductIds.Should().BeEquivalentTo(new[] { product1, product2 },
                "ValueComparer küme mutasyonunu tespit edip persist etmeli");
        }
    }
}
