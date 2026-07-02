using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;
using OrderHub.OrderProcessingService.Infrastructure.Saga;
using static OrderHub.OrderProcessingService.UnitTests.Saga.SagaTestEvents;

namespace OrderHub.OrderProcessingService.UnitTests.Saga;

/// <summary>
/// <see cref="OrderProcessingSaga"/> happy-path davranış testleri — MassTransit InMemory harness (broker'sız,
/// InMemory saga repo). Odak: ★ <b>fan-out sayımı (Karar B)</b> — çok-kalem (N=2) senaryoda saga, tüm
/// StockReserved gelene kadar İLERLEMEZ (ara-durumda bekler) ve redelivery çift-send üretmez. Gerçek DB
/// concurrency/RowVersion 5d-7 Testcontainers'ta; burada saga mantığı doğrulanır.
/// </summary>
public sealed class OrderProcessingSagaTests
{
    private static readonly TimeSpan NegativeWait = TimeSpan.FromMilliseconds(750);

    [Fact]
    public async Task TwoItems_ProgressesOnlyWhenAllReservedAndConfirmed_ReachesCompleted()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        // 1) OrderPlaced (2 kalem) → saga başlar, HER kalem için bir ReserveStock yayımlar.
        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingStockReservation)).Should().NotBeNull();

        var reserves = harness.Published.Select<ReserveStockIntegrationEvent>().ToList();
        reserves.Should().HaveCount(2, "her kalem için bir ReserveStock (fan-out)");
        reserves.Select(r => r.Context.Message.ProductId).Should().BeEquivalentTo(new[] { productA, productB });

        // 2) Yalnız ProductA reserved → ★ saga İLERLEMEZ (1/2). Bu fan-out sayımının kanıtı (N=1 olsaydı geçerdi).
        await harness.Bus.Publish(StockReserved(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingPayment, NegativeWait))
            .Should().BeNull("yalnız 1/2 rezervasyon geldi → saga beklemeli (ConfirmOrder/ProcessPayment YOK)");
        harness.Published.Select<ConfirmOrderIntegrationEvent>().Should().BeEmpty();
        harness.Published.Select<ProcessPaymentIntegrationEvent>().Should().BeEmpty();

        // 3) ProductB de reserved → küme ⊇ AllProductIds → ConfirmOrder + ProcessPayment, AwaitingPayment.
        await harness.Bus.Publish(StockReserved(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingPayment)).Should().NotBeNull();
        harness.Published.Select<ConfirmOrderIntegrationEvent>().Should().ContainSingle();
        var payment = harness.Published.Select<ProcessPaymentIntegrationEvent>().Should().ContainSingle().Subject;
        payment.Context.Message.OrderId.Should().Be(orderId);
        payment.Context.Message.Amount.Should().Be(199.50m, "tutar OrderPlaced'dan state'e saklanıp ProcessPayment'a taşınmalı");
        payment.Context.Message.Currency.Should().Be("TRY");

        // 4) PaymentSucceeded → HER kalem için ConfirmStockReservation (N=2) + MarkOrderPaid, AwaitingStockConfirmation.
        await harness.Bus.Publish(PaymentSucceeded(orderId));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingStockConfirmation)).Should().NotBeNull();
        harness.Published.Select<ConfirmStockReservationIntegrationEvent>().Should().HaveCount(2);
        harness.Published.Select<MarkOrderPaidIntegrationEvent>().Should().ContainSingle();

        // 5) Yalnız ProductA confirmed → ★ saga ship ETMEZ (1/2); ProductB confirmed → ShipOrder, Completed.
        await harness.Bus.Publish(StockReservationConfirmed(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.Completed, NegativeWait))
            .Should().BeNull("yalnız 1/2 onay geldi → saga ship etmemeli");
        harness.Published.Select<ShipOrderIntegrationEvent>().Should().BeEmpty();

        await harness.Bus.Publish(StockReservationConfirmed(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Completed)).Should().NotBeNull();
        harness.Published.Select<ShipOrderIntegrationEvent>().Should().ContainSingle();

        await harness.Stop();
    }

    [Fact]
    public async Task StockReservedRedelivered_DoesNotDoubleCountOrDoubleSend()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingStockReservation)).Should().NotBeNull();

        // ProductA'yı İKİ kez yayımla (at-least-once redelivery simülasyonu, inbox yok).
        await harness.Bus.Publish(StockReserved(orderId, productA));
        await harness.Bus.Publish(StockReserved(orderId, productA));

        // ★ Küme idempotent: {A} hâlâ ≠ {A,B} → saga ilerlemez, ÇİFT ConfirmOrder/ProcessPayment YOK.
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingPayment, NegativeWait)).Should().BeNull();
        harness.Published.Select<ConfirmOrderIntegrationEvent>().Should().BeEmpty();

        // ProductB gelince tam olur → tek ConfirmOrder + tek ProcessPayment (çift değil).
        await harness.Bus.Publish(StockReserved(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingPayment)).Should().NotBeNull();
        harness.Published.Select<ConfirmOrderIntegrationEvent>().Should().ContainSingle("küme guard çift-send'i önler");
        harness.Published.Select<ProcessPaymentIntegrationEvent>().Should().ContainSingle();

        await harness.Stop();
    }

    private static ServiceProvider BuildHarnessProvider() =>
        new ServiceCollection()
            .AddMassTransitTestHarness(configurator =>
                configurator.AddSagaStateMachine<OrderProcessingSaga, OrderProcessingSagaState>())
            .BuildServiceProvider(true);
}
