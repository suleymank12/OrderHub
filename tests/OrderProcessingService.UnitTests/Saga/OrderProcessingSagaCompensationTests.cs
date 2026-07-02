using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.OrderProcessingService.Infrastructure.Saga;
using static OrderHub.OrderProcessingService.UnitTests.Saga.SagaTestEvents;

namespace OrderHub.OrderProcessingService.UnitTests.Saga;

/// <summary>
/// <see cref="OrderProcessingSaga"/> compensation (mutsuz yol) davranış testleri — MassTransit InMemory harness.
/// Odak: ★ kısmi rezervasyon release (yalnız başarılı olanlar), payment-fail fan-in ara-durum, boş-release edge
/// (doğrudan cancel), geç/straggler event Ignore (KN-2), telafi idempotency, expired trigger (KN-3), doğru reason.
/// Gerçek broker/DB fidelity'si 5e-3 (Testcontainers).
/// </summary>
public sealed class OrderProcessingSagaCompensationTests
{
    private const string StockUnavailable = "stock_unavailable";
    private const string PaymentFailedReason = "payment_failed";
    private static readonly TimeSpan NegativeWait = TimeSpan.FromMilliseconds(750);

    [Fact]
    public async Task StockReservationFailed_PartialReservation_ReleasesReservedOnlyThenCancels()
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

        // ProductA reserved, ProductB stok yetersiz → telafi. ★ hedef = {A} (B rezerve olmadı).
        await harness.Bus.Publish(StockReserved(orderId, productA));
        await harness.Bus.Publish(StockReservationFailed(orderId, productB));

        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();
        var releases = harness.Published.Select<ReleaseStockIntegrationEvent>().ToList();
        releases.Should().ContainSingle("yalnız başarılı rezervasyon (A) release edilir — kısmi, AllProductIds değil");
        releases[0].Context.Message.ProductId.Should().Be(productA);
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().BeEmpty("release fan-in tamamlanmadan cancel yok");

        // StockReleased(A) → küme ⊇ hedef → CancelOrder(stock_unavailable) → Cancelled.
        await harness.Bus.Publish(StockReleased(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled)).Should().NotBeNull();
        var cancel = harness.Published.Select<CancelOrderIntegrationEvent>().Should().ContainSingle().Subject;
        cancel.Context.Message.OrderId.Should().Be(orderId);
        cancel.Context.Message.Reason.Should().Be(StockUnavailable);

        await harness.Stop();
    }

    [Fact]
    public async Task PaymentFailed_AllReserved_ReleasesAllThenCancels_FanInIntermediate()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        await harness.Bus.Publish(StockReserved(orderId, productA));
        await harness.Bus.Publish(StockReserved(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.AwaitingPayment)).Should().NotBeNull();

        // Ödeme reddedildi → TÜM rezervasyonlar (A+B) release.
        await harness.Bus.Publish(PaymentFailed(orderId));
        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();
        var releases = harness.Published.Select<ReleaseStockIntegrationEvent>().ToList();
        releases.Should().HaveCount(2);
        releases.Select(release => release.Context.Message.ProductId).Should().BeEquivalentTo(new[] { productA, productB });

        // ★ Fan-in ara-durum: 1/2 released iken Compensating'te bekler, cancel YOK.
        await harness.Bus.Publish(StockReleased(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled, NegativeWait))
            .Should().BeNull("yalnız 1/2 release geldi → saga beklemeli");
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().BeEmpty();

        // 2/2 released → CancelOrder(payment_failed) → Cancelled.
        await harness.Bus.Publish(StockReleased(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled)).Should().NotBeNull();
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().ContainSingle()
            .Which.Context.Message.Reason.Should().Be(PaymentFailedReason);

        await harness.Stop();
    }

    [Fact]
    public async Task StockReservationFailed_NoReservationsYet_CancelsDirectlyWithoutRelease()
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

        // Hiç rezervasyon gelmeden ilk ürün fail → hedef boş → doğrudan cancel (release yok), Compensating atlanır.
        await harness.Bus.Publish(StockReservationFailed(orderId, productB));

        (await sagaHarness.Exists(orderId, saga => saga.Cancelled)).Should().NotBeNull();
        harness.Published.Select<ReleaseStockIntegrationEvent>().Should().BeEmpty("release edilecek başarılı rezervasyon yok");
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().ContainSingle()
            .Which.Context.Message.Reason.Should().Be(StockUnavailable);

        await harness.Stop();
    }

    [Fact]
    public async Task Compensating_LateStockReserved_IsIgnored_NoExtraReleaseOrCancel()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        await harness.Bus.Publish(StockReserved(orderId, productA));
        await harness.Bus.Publish(StockReservationFailed(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();

        // ★ KN-2: geç/straggler StockReserved(B) Compensating'te → Ignore (o rezervasyon Inventory 15dk expiry
        // backstop'una bırakılır; saga aktif release ETMEZ). Ekstra release/cancel yok, state değişmez.
        await harness.Bus.Publish(StockReserved(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled, NegativeWait))
            .Should().BeNull("geç StockReserved cancel tetiklememeli");
        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();
        harness.Published.Select<ReleaseStockIntegrationEvent>().Should().ContainSingle("yalnız A release (B için ek release yok)");

        await harness.Stop();
    }

    [Fact]
    public async Task StockReservationFailedTwice_IsIdempotent_SingleReleaseAndSingleCancel()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        await harness.Bus.Publish(StockReserved(orderId, productA));

        // Aynı fail İKİ kez (redelivery). İlki → Compensating + release(A); ikincisi → Compensating'te Ignore.
        await harness.Bus.Publish(StockReservationFailed(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();
        await harness.Bus.Publish(StockReservationFailed(orderId, productB));

        await harness.Bus.Publish(StockReleased(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled)).Should().NotBeNull();

        harness.Published.Select<ReleaseStockIntegrationEvent>().Should().ContainSingle("çift fail tek release üretir");
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().ContainSingle("çift fail tek cancel üretir");

        await harness.Stop();
    }

    [Fact]
    public async Task StockReservationExpired_TriggersCompensation_LikeFailure()
    {
        var orderId = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderProcessingSaga, OrderProcessingSagaState>>();

        await harness.Bus.Publish(OrderPlaced(orderId, productA, productB));
        await harness.Bus.Publish(StockReserved(orderId, productA));

        // ★ KN-3: expired = failure → aynı telafi dalı.
        await harness.Bus.Publish(StockReservationExpired(orderId, productB));
        (await sagaHarness.Exists(orderId, saga => saga.Compensating)).Should().NotBeNull();
        harness.Published.Select<ReleaseStockIntegrationEvent>().Should().ContainSingle()
            .Which.Context.Message.ProductId.Should().Be(productA);

        await harness.Bus.Publish(StockReleased(orderId, productA));
        (await sagaHarness.Exists(orderId, saga => saga.Cancelled)).Should().NotBeNull();
        harness.Published.Select<CancelOrderIntegrationEvent>().Should().ContainSingle()
            .Which.Context.Message.Reason.Should().Be(StockUnavailable);

        await harness.Stop();
    }

    private static ServiceProvider BuildHarnessProvider() =>
        new ServiceCollection()
            .AddMassTransitTestHarness(configurator =>
                configurator.AddSagaStateMachine<OrderProcessingSaga, OrderProcessingSagaState>())
            .BuildServiceProvider(true);
}
