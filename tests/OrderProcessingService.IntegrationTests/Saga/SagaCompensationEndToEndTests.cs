using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;
using OrderHub.OrderProcessingService.IntegrationTests.Fixtures;
using OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga;

/// <summary>
/// Faz 5 5e-3 — saga <b>compensation</b> (mutsuz yol) uçtan uca, <b>gerçek RabbitMQ + gerçek SQL</b>. InMemory harness
/// (5e-2) sayaç/transition mantığını test etti; burada gerçek broker tip→exchange→queue routing (ReleaseStock/
/// CancelOrder dâhil) + gerçek EF compensation state persistence (ProductsToRelease/ReleasedProductIds/
/// CancellationReason JSON round-trip) doğrulanır. Saga bus'ı prod DI; diğer servisler test-double consumer.
/// <para>
/// ★ <b>Dürüst sınır:</b> Bu testler compensation happy-flow'unu (fail → release → cancel) gerçek altyapıda
/// kanıtlar — <b>contention/retry DEĞİL</b> (o Test A / <see cref="RowVersionConcurrencyTests"/>). Timing-bağımsız:
/// her assert öncesi ilgili command'in pozitif sinyali bounded-wait ile beklenir (ReleaseStock ve CancelOrder ayrı
/// asenkron teslim → ikisi de beklenir), sonra sayı/state doğrulanır (5d-7 flaky dersi — sahte-yeşil/flaky yok).
/// </para>
/// </summary>
[Collection(SagaEndToEndCollection.Name)]
public sealed class SagaCompensationEndToEndTests(
    SagasSqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    private const string StockUnavailable = "stock_unavailable";
    private const string PaymentFailedReason = "payment_failed";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task PaymentFailed_ReleasesAllThenCancels_CompensationStatePersistedInSql()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var harness = new SagaBusHarness(sql, rabbit);
        using var cts = new CancellationTokenSource(Timeout);
        await harness.StartAsync(cts.Token);

        await harness.PublishAsync(SagaTestMessages.OrderPlaced(orderId, productA, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ReserveStockIntegrationEvent>(2, cts.Token);
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productA), cts.Token);
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ConfirmOrderIntegrationEvent>(1, cts.Token);
        await harness.Recorder.WaitForCountAsync<ProcessPaymentIntegrationEvent>(1, cts.Token);
        await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "AwaitingPayment", cts.Token);

        // ★ Ödeme reddedildi → saga TÜM rezervasyonları (AllProductIds) release eder.
        await harness.PublishAsync(SagaTestMessages.PaymentFailed(orderId), cts.Token);
        var releases = await harness.Recorder.WaitForCountAsync<ReleaseStockIntegrationEvent>(2, cts.Token);
        releases.Select(release => release.ProductId).Should().BeEquivalentTo(new[] { productA, productB });
        var compensating = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Compensating", cts.Token);
        compensating.ProductsToRelease.Should().BeEquivalentTo(new[] { productA, productB });
        compensating.CancellationReason.Should().Be(PaymentFailedReason);

        // ★ Fan-in ara-durum (gerçek SQL): 1/2 released → Compensating'te bekler, CancelOrder YOK.
        await harness.PublishAsync(SagaTestMessages.StockReleased(orderId, productA), cts.Token);
        var oneReleased = await harness.WaitForSagaAsync(
            orderId, saga => saga.ReleasedProductIds.Contains(productA), cts.Token);
        oneReleased.CurrentState.Should().Be("Compensating", "yalnız 1/2 release → saga beklemeli");
        harness.Recorder.Count<CancelOrderIntegrationEvent>().Should().Be(0);

        // 2/2 released → CancelOrder(payment_failed) → Cancelled (gerçek SQL'de).
        await harness.PublishAsync(SagaTestMessages.StockReleased(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<CancelOrderIntegrationEvent>(1, cts.Token);
        var cancelled = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Cancelled", cts.Token);
        cancelled.ReleasedProductIds.Should().BeEquivalentTo(new[] { productA, productB });
        var cancel = harness.Recorder.Snapshot<CancelOrderIntegrationEvent>().Single();
        cancel.OrderId.Should().Be(orderId);
        cancel.Reason.Should().Be(PaymentFailedReason);
    }

    [Fact]
    public async Task StockReservationFailed_Partial_ReleasesReservedOnly_ProvesNoStockLeakInRealInfra()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var harness = new SagaBusHarness(sql, rabbit);
        using var cts = new CancellationTokenSource(Timeout);
        await harness.StartAsync(cts.Token);

        await harness.PublishAsync(SagaTestMessages.OrderPlaced(orderId, productA, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ReserveStockIntegrationEvent>(2, cts.Token);

        // ★ Sadece A reserved (B fail). A'nın işlendiğini bekle → sonra B'nin fail'ini publish et (deterministik sıra).
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productA), cts.Token);
        await harness.WaitForSagaAsync(orderId, saga => saga.ReservedProductIds.Contains(productA), cts.Token);
        await harness.PublishAsync(SagaTestMessages.StockReservationFailed(orderId, productB), cts.Token);

        // ★ Saga SADECE A'yı release eder (B rezerve olmadı) — ADR stok-sızıntısı hatasının gerçek-altyapı kanıtı.
        var releases = await harness.Recorder.WaitForCountAsync<ReleaseStockIntegrationEvent>(1, cts.Token);
        releases.Single().ProductId.Should().Be(productA);
        var compensating = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Compensating", cts.Token);
        compensating.ProductsToRelease.Should().BeEquivalentTo(new[] { productA }, "partial: yalnız A, B değil");
        compensating.CancellationReason.Should().Be(StockUnavailable);

        // StockReleased(A) → CancelOrder(stock_unavailable) → Cancelled.
        await harness.PublishAsync(SagaTestMessages.StockReleased(orderId, productA), cts.Token);
        await harness.Recorder.WaitForCountAsync<CancelOrderIntegrationEvent>(1, cts.Token);
        var cancelled = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Cancelled", cts.Token);
        cancelled.ProductsToRelease.Should().BeEquivalentTo(new[] { productA });
        cancelled.ReleasedProductIds.Should().BeEquivalentTo(new[] { productA });
        harness.Recorder.Count<ReleaseStockIntegrationEvent>().Should().Be(1, "yalnız reserved A release edilir, B değil (sızıntı yok)");
        harness.Recorder.Snapshot<CancelOrderIntegrationEvent>().Single().Reason.Should().Be(StockUnavailable);
    }

    [Fact]
    public async Task StockReservationFailed_NoReservations_CancelsDirectly_NoStuckSagaInRealInfra()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var harness = new SagaBusHarness(sql, rabbit);
        using var cts = new CancellationTokenSource(Timeout);
        await harness.StartAsync(cts.Token);

        await harness.PublishAsync(SagaTestMessages.OrderPlaced(orderId, productA, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ReserveStockIntegrationEvent>(2, cts.Token);
        await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "AwaitingStockReservation", cts.Token);

        // ★ Hiç StockReserved gelmeden fail → ProductsToRelease boş → saga hiç release göndermez, doğrudan cancel.
        // Gerçek broker'da stuck-saga OLMADIĞININ kanıtı (5e-2 boş-release edge çözümü gerçekte de çalışır).
        await harness.PublishAsync(SagaTestMessages.StockReservationFailed(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<CancelOrderIntegrationEvent>(1, cts.Token);
        var cancelled = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Cancelled", cts.Token);
        cancelled.ProductsToRelease.Should().BeEmpty();
        cancelled.ReleasedProductIds.Should().BeEmpty();
        harness.Recorder.Count<ReleaseStockIntegrationEvent>().Should().Be(0, "release edilecek rezervasyon yok → stuck-saga yok");
        harness.Recorder.Snapshot<CancelOrderIntegrationEvent>().Single().Reason.Should().Be(StockUnavailable);
    }
}
