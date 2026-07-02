using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.Infrastructure.Saga;

/// <summary>
/// OrderProcessingSaga — sipariş işleme orkestrasyonu (ADR-0007 Karar 4/5). <b>Happy path</b> (5d-4b) +
/// <b>compensation</b> (5e-2, mutsuz yol).
/// <para>
/// <b>Happy:</b> OrderPlaced → N× ReserveStock → (hepsi reserved) ConfirmOrder + ProcessPayment → PaymentSucceeded →
/// N× ConfirmStockReservation + MarkOrderPaid → (hepsi confirmed) ShipOrder → Completed.
/// </para>
/// <para>
/// <b>Compensation:</b> AwaitingStockReservation'da StockReservationFailed/Expired → o ana kadar başarılı rezervasyonları
/// (<see cref="OrderProcessingSagaState.ReservedProductIds"/> snapshot'ı) release; AwaitingPayment'ta PaymentFailed →
/// TÜM rezervasyonları (AllProductIds) release → <see cref="Compensating"/>'te StockReleased fan-in beklenir (küme
/// guard, happy-path deseni) → tamamlanınca CancelOrder → <see cref="Cancelled"/>. Hiç rezervasyon yoksa release
/// atlanır, doğrudan CancelOrder. AwaitingStockConfirmation <b>forward-only</b> (ödeme başarılı, order Paid → iptal
/// edilemez/refund gerekir → kapsam dışı). Fan-out/fan-in ve iptal, küme + status-guard ile <b>idempotent</b>
/// (redelivery çift-send üretmez); geç/straggler event'ler <c>Ignore</c> edilir (KN-2: geç-reserved yetim kalmaz →
/// Inventory 15dk expiry backstop serbest bırakır). Eşzamanlı contention RowVersion optimistic + retry ile serileşir.
/// </para>
/// </summary>
internal sealed class OrderProcessingSaga : MassTransitStateMachine<OrderProcessingSagaState>
{
    // OrderService.OrderCancellationReasons ile AYNI değerler (cross-service Contracts sabiti yok → burada mirror'lanır).
    private const string StockUnavailableReason = "stock_unavailable";
    private const string PaymentFailedReason = "payment_failed";

    public OrderProcessingSaga()
    {
        InstanceState(instance => instance.CurrentState);

        // Tüm olaylar OrderId ile correlate olur (saga örneği = sipariş).
        Event(() => OrderPlaced, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReserved, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReservationConfirmed, x => x.CorrelateById(context => context.Message.OrderId));
        // Compensation event'leri (5e-2).
        Event(() => StockReservationFailed, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReservationExpired, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReleased, x => x.CorrelateById(context => context.Message.OrderId));

        Initially(
            When(OrderPlaced)
                .Then(StoreOrderDetails)
                .ThenAsync(SendStockReservations)
                .TransitionTo(AwaitingStockReservation));

        During(AwaitingStockReservation,
            When(StockReserved)
                .Then(AddReservedProduct)
                .If(AllStockReserved, binder => binder
                    .ThenAsync(ConfirmOrderAndProcessPayment)
                    .TransitionTo(AwaitingPayment)),
            // Stok ayrılamadı / rezervasyon zaman aşımı (KN-3: expired = failure) → başarılı olanları release et.
            When(StockReservationFailed).Then(EnterStockCompensation).IfElse(NothingToRelease,
                empty => empty.ThenAsync(SendCancelOrder).TransitionTo(Cancelled),
                some => some.ThenAsync(SendReleases).TransitionTo(Compensating)),
            When(StockReservationExpired).Then(EnterStockCompensation).IfElse(NothingToRelease,
                empty => empty.ThenAsync(SendCancelOrder).TransitionTo(Cancelled),
                some => some.ThenAsync(SendReleases).TransitionTo(Compensating)));

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .ThenAsync(ConfirmReservationsAndMarkPaid)
                .TransitionTo(AwaitingStockConfirmation),
            // Ödeme reddedildi → TÜM rezervasyonlar (AllProductIds, boş olamaz — order kalemli) release et.
            When(PaymentFailed)
                .Then(EnterPaymentCompensation)
                .ThenAsync(SendReleases)
                .TransitionTo(Compensating),
            // Tüm rezervasyonlar geldi; geç/yeniden-teslim → no-op (Karar B + state guard).
            Ignore(StockReserved),
            Ignore(StockReservationFailed),
            Ignore(StockReservationExpired));

        During(AwaitingStockConfirmation,
            When(StockReservationConfirmed)
                .Then(AddConfirmedProduct)
                .If(AllStockConfirmed, binder => binder
                    .ThenAsync(SendShipOrder)
                    .TransitionTo(Completed)),
            // Forward-only: ödeme başarılı (order Paid) → compensation YOK. Geç/yeniden-teslim event'ler no-op.
            Ignore(StockReserved),
            Ignore(PaymentSucceeded),
            Ignore(PaymentFailed),
            Ignore(StockReservationFailed),
            Ignore(StockReservationExpired));

        During(Compensating,
            When(StockReleased)
                .Then(AddReleasedProduct)
                .If(AllStockReleased, binder => binder
                    .ThenAsync(SendCancelOrder)
                    .TransitionTo(Cancelled)),
            // Idempotency + straggler (KN-2): tekrar-tetikleyici ve geç happy event'ler no-op (çift release/cancel yok).
            Ignore(StockReserved),
            Ignore(StockReservationConfirmed),
            Ignore(PaymentSucceeded),
            Ignore(StockReservationFailed),
            Ignore(StockReservationExpired),
            Ignore(PaymentFailed));

        During(Cancelled,
            Ignore(StockReserved),
            Ignore(StockReservationConfirmed),
            Ignore(PaymentSucceeded),
            Ignore(StockReleased),
            Ignore(StockReservationFailed),
            Ignore(StockReservationExpired),
            Ignore(PaymentFailed));

        During(Completed,
            Ignore(StockReserved),
            Ignore(PaymentSucceeded),
            Ignore(StockReservationConfirmed));
    }

    /// <summary>Tüm kalemlerin stok rezervasyonu beklenir (OrderPlaced sonrası).</summary>
    public State AwaitingStockReservation { get; private set; } = null!;

    /// <summary>Ödeme sonucu beklenir (rezervasyon tamam + ProcessPayment gönderildikten sonra).</summary>
    public State AwaitingPayment { get; private set; } = null!;

    /// <summary>Tüm kalemlerin rezervasyon onayı beklenir (ödeme başarılı sonrası).</summary>
    public State AwaitingStockConfirmation { get; private set; } = null!;

    /// <summary>Sipariş kargolandı; saga tamamlandı (instance audit için saklanır — Finalize/silme YOK).</summary>
    public State Completed { get; private set; } = null!;

    /// <summary>Telafi: rezervasyonlar serbest bırakılıyor; StockReleased fan-in beklenir (5e-2).</summary>
    public State Compensating { get; private set; } = null!;

    /// <summary>Sipariş iptal edildi (telafi tamamlandı); terminal — audit için saklanır (Completed deseni, Finalize YOK).</summary>
    public State Cancelled { get; private set; } = null!;

    /// <summary>Saga tetik olayı (OrderService → saga): sipariş yerleşti.</summary>
    public Event<OrderPlacedIntegrationEvent> OrderPlaced { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün stoğu ayrıldı (fan-in).</summary>
    public Event<StockReservedIntegrationEvent> StockReserved { get; private set; } = null!;

    /// <summary>PaymentService → saga: ödeme başarıyla tamamlandı.</summary>
    public Event<PaymentSucceededIntegrationEvent> PaymentSucceeded { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün rezervasyonu onaylandı (fan-in).</summary>
    public Event<StockReservationConfirmedIntegrationEvent> StockReservationConfirmed { get; private set; } = null!;

    /// <summary>InventoryService → saga: stok ayrılamadı (yetersiz stok) → compensation.</summary>
    public Event<StockReservationFailedIntegrationEvent> StockReservationFailed { get; private set; } = null!;

    /// <summary>InventoryService → saga: rezervasyon zaman aşımına uğradı (Hangfire) → compensation (KN-3).</summary>
    public Event<StockReservationExpiredIntegrationEvent> StockReservationExpired { get; private set; } = null!;

    /// <summary>PaymentService → saga: ödeme reddedildi → compensation.</summary>
    public Event<PaymentFailedIntegrationEvent> PaymentFailed { get; private set; } = null!;

    /// <summary>InventoryService → saga: rezervasyon serbest bırakıldı (compensation ack, fan-in).</summary>
    public Event<StockReleasedIntegrationEvent> StockReleased { get; private set; } = null!;

    private static void StoreOrderDetails(BehaviorContext<OrderProcessingSagaState, OrderPlacedIntegrationEvent> context)
    {
        var message = context.Message;
        var saga = context.Saga;
        saga.CustomerId = message.CustomerId;
        saga.Amount = message.Amount;
        saga.Currency = message.Currency;
        saga.ItemCount = message.Items.Count;
        saga.AllProductIds = message.Items.Select(item => item.ProductId).ToHashSet();
    }

    private static async Task SendStockReservations(BehaviorContext<OrderProcessingSagaState, OrderPlacedIntegrationEvent> context)
    {
        foreach (var item in context.Message.Items)
        {
            await context.Publish(new ReserveStockIntegrationEvent
            {
                Id = NewId.NextGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = context.Saga.CorrelationId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }
    }

    // Küme Add idempotent (aynı ProductId yeniden gelirse no-op) → redelivery doğal güvenli (Karar B).
    private static void AddReservedProduct(BehaviorContext<OrderProcessingSagaState, StockReservedIntegrationEvent> context) =>
        context.Saga.ReservedProductIds.Add(context.Message.ProductId);

    private static void AddConfirmedProduct(BehaviorContext<OrderProcessingSagaState, StockReservationConfirmedIntegrationEvent> context) =>
        context.Saga.ConfirmedProductIds.Add(context.Message.ProductId);

    private static bool AllStockReserved(BehaviorContext<OrderProcessingSagaState, StockReservedIntegrationEvent> context) =>
        context.Saga.ReservedProductIds.IsSupersetOf(context.Saga.AllProductIds);

    private static bool AllStockConfirmed(BehaviorContext<OrderProcessingSagaState, StockReservationConfirmedIntegrationEvent> context) =>
        context.Saga.ConfirmedProductIds.IsSupersetOf(context.Saga.AllProductIds);

    private static async Task ConfirmOrderAndProcessPayment(BehaviorContext<OrderProcessingSagaState, StockReservedIntegrationEvent> context)
    {
        var saga = context.Saga;
        await context.Publish(new ConfirmOrderIntegrationEvent
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = saga.CorrelationId,
        });
        await context.Publish(new ProcessPaymentIntegrationEvent
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = saga.CorrelationId,
            CustomerId = saga.CustomerId,
            Amount = saga.Amount,
            Currency = saga.Currency,
        });
    }

    private static async Task ConfirmReservationsAndMarkPaid(BehaviorContext<OrderProcessingSagaState, PaymentSucceededIntegrationEvent> context)
    {
        var saga = context.Saga;
        foreach (var productId in saga.AllProductIds)
        {
            await context.Publish(new ConfirmStockReservationIntegrationEvent
            {
                Id = NewId.NextGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = saga.CorrelationId,
                ProductId = productId,
            });
        }

        await context.Publish(new MarkOrderPaidIntegrationEvent
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = saga.CorrelationId,
        });
    }

    private static Task SendShipOrder(BehaviorContext<OrderProcessingSagaState, StockReservationConfirmedIntegrationEvent> context) =>
        context.Publish(new ShipOrderIntegrationEvent
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = context.Saga.CorrelationId,
        });

    // --- Compensation (5e-2) ---

    // Hedef release setini DONDUR (snapshot): o ana kadar başarılı rezervasyonlar. Geç event'ler bunu değiştirmez.
    private static void EnterStockCompensation<TEvent>(BehaviorContext<OrderProcessingSagaState, TEvent> context)
        where TEvent : class
    {
        context.Saga.ProductsToRelease = [.. context.Saga.ReservedProductIds];
        context.Saga.CancellationReason = StockUnavailableReason;
    }

    // Ödeme-fail: tüm rezervasyonlar başarılıydı → AllProductIds release edilir.
    private static void EnterPaymentCompensation(BehaviorContext<OrderProcessingSagaState, PaymentFailedIntegrationEvent> context)
    {
        context.Saga.ProductsToRelease = [.. context.Saga.AllProductIds];
        context.Saga.CancellationReason = PaymentFailedReason;
    }

    private static bool NothingToRelease<TEvent>(BehaviorContext<OrderProcessingSagaState, TEvent> context)
        where TEvent : class => context.Saga.ProductsToRelease.Count == 0;

    private static async Task SendReleases<TEvent>(BehaviorContext<OrderProcessingSagaState, TEvent> context)
        where TEvent : class
    {
        foreach (var productId in context.Saga.ProductsToRelease)
        {
            await context.Publish(new ReleaseStockIntegrationEvent
            {
                Id = NewId.NextGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                OrderId = context.Saga.CorrelationId,
                ProductId = productId,
            });
        }
    }

    private static void AddReleasedProduct(BehaviorContext<OrderProcessingSagaState, StockReleasedIntegrationEvent> context) =>
        context.Saga.ReleasedProductIds.Add(context.Message.ProductId);

    private static bool AllStockReleased(BehaviorContext<OrderProcessingSagaState, StockReleasedIntegrationEvent> context) =>
        context.Saga.ReleasedProductIds.IsSupersetOf(context.Saga.ProductsToRelease);

    private static Task SendCancelOrder<TEvent>(BehaviorContext<OrderProcessingSagaState, TEvent> context)
        where TEvent : class =>
        context.Publish(new CancelOrderIntegrationEvent
        {
            Id = NewId.NextGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            OrderId = context.Saga.CorrelationId,
            Reason = context.Saga.CancellationReason!,
        });
}
