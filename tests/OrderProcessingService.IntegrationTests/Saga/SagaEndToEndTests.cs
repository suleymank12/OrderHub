using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;
using OrderHub.OrderProcessingService.IntegrationTests.Fixtures;
using OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga;

/// <summary>
/// Faz 5 5d-7 <b>Test B</b> — saga uçtan uca akışı <b>gerçek RabbitMQ + gerçek SQL</b> ile (InMemory harness
/// 5d-4b'de yapıldı; burada InMemory'nin YAPAMADIĞI kanıtlanır: gerçek broker tip→exchange→queue routing,
/// gerçek EF saga persistence + <c>GuidSetJsonConverter</c> round-trip, serialization). Saga bus'ı prod DI
/// (<see cref="SagaBusHarness"/>); diğer servisler test-double consumer ile temsil edilir.
/// <para>
/// ★ <b>Dürüst sınır:</b> Bu testler <b>final tutarlılık + fan-out tamamlanması</b> kanıtıdır,
/// <b>contention/retry kanıtı DEĞİL</b> (o Test A / <see cref="RowVersionConcurrencyTests"/>). İki StockReserved
/// sıralı işlense de testler doğru şekilde geçer — kasıtlı olarak fan-out sayımı ve gerçek DB state'i doğrularlar.
/// Ara-durum assertion'ları (1/2 rezerve iken saga bekliyor) fan-out'un gerçek altyapıda çalıştığını gösterir.
/// </para>
/// </summary>
[Collection(SagaEndToEndCollection.Name)]
public sealed class SagaEndToEndTests(
    SagasSqlServerContainerFixture sql,
    RabbitMqContainerFixture rabbit)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task TwoItemFanOut_OverRealBrokerAndSql_ReachesCompletedWithPersistedSets()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var harness = new SagaBusHarness(sql, rabbit);
        using var cts = new CancellationTokenSource(Timeout);
        await harness.StartAsync(cts.Token);

        // 1) OrderPlaced (2 kalem) → gerçek broker üzerinden HER kalem için bir ReserveStock (fan-out send).
        await harness.PublishAsync(SagaTestMessages.OrderPlaced(orderId, productA, productB), cts.Token);
        var reserves = await harness.Recorder.WaitForCountAsync<ReserveStockIntegrationEvent>(2, cts.Token);
        reserves.Select(reserve => reserve.ProductId).Should().BeEquivalentTo(new[] { productA, productB });

        // Saga satırı gerçek SQL'de oluştu: AwaitingStockReservation, henüz rezervasyon kümesi boş.
        var afterPlaced = await harness.WaitForSagaAsync(
            orderId, saga => saga.CurrentState == "AwaitingStockReservation", cts.Token);
        afterPlaced.AllProductIds.Should().BeEquivalentTo(new[] { productA, productB });
        afterPlaced.ReservedProductIds.Should().BeEmpty();

        // 2) ★ 1/2 reserved → saga İLERLEMEZ (gerçek DB state'te doğrula); ConfirmOrder/ProcessPayment YOK.
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productA), cts.Token);
        var oneReserved = await harness.WaitForSagaAsync(
            orderId, saga => saga.ReservedProductIds.Contains(productA), cts.Token);
        oneReserved.CurrentState.Should().Be("AwaitingStockReservation", "yalnız 1/2 rezervasyon → saga beklemeli");
        harness.Recorder.Count<ConfirmOrderIntegrationEvent>().Should().Be(0);
        harness.Recorder.Count<ProcessPaymentIntegrationEvent>().Should().Be(0);

        // 3) 2/2 reserved → küme ⊇ AllProductIds → ConfirmOrder + ProcessPayment, AwaitingPayment.
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ConfirmOrderIntegrationEvent>(1, cts.Token);
        var payment = (await harness.Recorder.WaitForCountAsync<ProcessPaymentIntegrationEvent>(1, cts.Token)).Single();
        payment.Amount.Should().Be(199.50m, "tutar OrderPlaced'dan state'e saklanıp ProcessPayment'a taşınmalı");
        payment.Currency.Should().Be("TRY");
        var reservedAll = await harness.WaitForSagaAsync(
            orderId, saga => saga.CurrentState == "AwaitingPayment", cts.Token);
        reservedAll.ReservedProductIds.Should().BeEquivalentTo(new[] { productA, productB });

        // 4) PaymentSucceeded → HER kalem için ConfirmStockReservation (N=2) + MarkOrderPaid, AwaitingStockConfirmation.
        await harness.PublishAsync(SagaTestMessages.PaymentSucceeded(orderId), cts.Token);
        await harness.Recorder.WaitForCountAsync<ConfirmStockReservationIntegrationEvent>(2, cts.Token);
        await harness.Recorder.WaitForCountAsync<MarkOrderPaidIntegrationEvent>(1, cts.Token);
        await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "AwaitingStockConfirmation", cts.Token);

        // 5) ★ 1/2 confirmed → saga ship ETMEZ (gerçek DB state); ShipOrder YOK.
        await harness.PublishAsync(SagaTestMessages.StockReservationConfirmed(orderId, productA), cts.Token);
        var oneConfirmed = await harness.WaitForSagaAsync(
            orderId, saga => saga.ConfirmedProductIds.Contains(productA), cts.Token);
        oneConfirmed.CurrentState.Should().Be("AwaitingStockConfirmation", "yalnız 1/2 onay → saga ship etmemeli");
        harness.Recorder.Count<ShipOrderIntegrationEvent>().Should().Be(0);

        // 6) 2/2 confirmed → ShipOrder, Completed. ★ Fan-out kümeleri gerçek SQL'de round-trip (GuidSetJsonConverter).
        await harness.PublishAsync(SagaTestMessages.StockReservationConfirmed(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ShipOrderIntegrationEvent>(1, cts.Token);
        var completed = await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "Completed", cts.Token);
        completed.ReservedProductIds.Should().BeEquivalentTo(new[] { productA, productB });
        completed.ConfirmedProductIds.Should().BeEquivalentTo(new[] { productA, productB },
            "GuidSetJsonConverter fan-out kümelerini gerçek SQL'de round-trip etmeli");
        harness.Recorder.Count<ConfirmOrderIntegrationEvent>().Should().Be(1, "çift-send yok");
    }

    [Fact]
    public async Task StockReservedRedeliveredOverRealBroker_IdempotentSetNoDoubleSend()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var harness = new SagaBusHarness(sql, rabbit);
        using var cts = new CancellationTokenSource(Timeout);
        await harness.StartAsync(cts.Token);

        await harness.PublishAsync(SagaTestMessages.OrderPlaced(orderId, productA, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ReserveStockIntegrationEvent>(2, cts.Token);

        // ★ Aynı StockReserved'ı İKİ kez yayımla (at-least-once redelivery, gerçek broker; saga'da inbox yok).
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productA), cts.Token);
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productA), cts.Token);

        // Küme Add idempotent → gerçek DB'de {A} (çift sayım yok), saga hâlâ bekliyor (1/2).
        var reserved = await harness.WaitForSagaAsync(
            orderId, saga => saga.ReservedProductIds.Contains(productA), cts.Token);
        reserved.ReservedProductIds.Should().BeEquivalentTo(new[] { productA }, "küme idempotent → redelivery çift saymaz");
        reserved.CurrentState.Should().Be("AwaitingStockReservation");
        harness.Recorder.Count<ConfirmOrderIntegrationEvent>().Should().Be(0);

        // ProductB gelince tam olur → TEK ConfirmOrder + TEK ProcessPayment (redelivery'e rağmen çift yok).
        // ★ İkisinin de pozitif sinyalini bekle (gerçek broker'da bağımsız/asenkron teslim; biri geç kalabilir),
        // SONRA "tam olarak 1" doğrula → çift-send yok (küme guard ikinci kez üretmez, sayı 1'de kalır → deterministik).
        await harness.PublishAsync(SagaTestMessages.StockReserved(orderId, productB), cts.Token);
        await harness.Recorder.WaitForCountAsync<ConfirmOrderIntegrationEvent>(1, cts.Token);
        await harness.Recorder.WaitForCountAsync<ProcessPaymentIntegrationEvent>(1, cts.Token);
        await harness.WaitForSagaAsync(orderId, saga => saga.CurrentState == "AwaitingPayment", cts.Token);
        harness.Recorder.Count<ConfirmOrderIntegrationEvent>().Should().Be(1, "küme guard gerçek broker'da çift-send'i önler");
        harness.Recorder.Count<ProcessPaymentIntegrationEvent>().Should().Be(1);
    }
}
