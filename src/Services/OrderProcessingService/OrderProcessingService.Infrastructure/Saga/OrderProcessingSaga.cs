using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.Infrastructure.Saga;

/// <summary>
/// OrderProcessingSaga — sipariş işleme orkestrasyonu (ADR-0007 Karar 4), <b>happy path</b> (compensation 5e).
/// Akış: OrderPlaced → N× ReserveStock → (hepsi reserved) ConfirmOrder + ProcessPayment → PaymentSucceeded →
/// N× ConfirmStockReservation + MarkOrderPaid → (hepsi confirmed) ShipOrder → Completed.
/// <para>
/// <b>Fan-out (Karar B):</b> sayım <see cref="OrderProcessingSagaState"/> ProductId <b>kümeleri</b> ile —
/// guard "küme ⊇ AllProductIds" (<c>IsSupersetOf</c>). Küme <c>Add</c> idempotent olduğundan aynı StockReserved'ın
/// yeniden teslimi (inbox yok, at-least-once) sayıyı bozmaz; ayrıca state ilerledikten sonra geç gelen fan-in
/// olayları açıkça <c>Ignore</c> edilir → <b>çift-send YOK</b>. <b>Mesajlar Publish</b> edilir (ADR-0004
/// tip-bazlı routing, her komutun tek tüketicisi → EndpointConvention'sız point-to-point). Eşzamanlı mesaj
/// contention'ı RowVersion optimistic + retry ile (5d-4a wiring).
/// </para>
/// <para><b>Karar D:</b> ConfirmOrder + ProcessPayment paralel gönderilir; <c>MarkOrderPaid</c> sipariş henüz
/// Confirmed değilse OrderService consumer'ında retry'a güvenir (5d-5). <b>Compensation</b>
/// (<c>StockReservationFailed</c>/<c>PaymentFailed</c>) bu adımda YOK → <b>5e</b>.</para>
/// </summary>
internal sealed class OrderProcessingSaga : MassTransitStateMachine<OrderProcessingSagaState>
{
    public OrderProcessingSaga()
    {
        InstanceState(instance => instance.CurrentState);

        // Tüm olaylar OrderId ile correlate olur (saga örneği = sipariş).
        Event(() => OrderPlaced, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReserved, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReservationConfirmed, x => x.CorrelateById(context => context.Message.OrderId));

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
                    .TransitionTo(AwaitingPayment)));

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .ThenAsync(ConfirmReservationsAndMarkPaid)
                .TransitionTo(AwaitingStockConfirmation),
            // Tüm rezervasyonlar geldi; geç/yeniden-teslim StockReserved → no-op (Karar B + state guard).
            Ignore(StockReserved));

        During(AwaitingStockConfirmation,
            When(StockReservationConfirmed)
                .Then(AddConfirmedProduct)
                .If(AllStockConfirmed, binder => binder
                    .ThenAsync(SendShipOrder)
                    .TransitionTo(Completed)),
            Ignore(StockReserved),
            Ignore(PaymentSucceeded));

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

    /// <summary>Saga tetik olayı (OrderService → saga): sipariş yerleşti.</summary>
    public Event<OrderPlacedIntegrationEvent> OrderPlaced { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün stoğu ayrıldı (fan-in).</summary>
    public Event<StockReservedIntegrationEvent> StockReserved { get; private set; } = null!;

    /// <summary>PaymentService → saga: ödeme başarıyla tamamlandı.</summary>
    public Event<PaymentSucceededIntegrationEvent> PaymentSucceeded { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün rezervasyonu onaylandı (fan-in).</summary>
    public Event<StockReservationConfirmedIntegrationEvent> StockReservationConfirmed { get; private set; } = null!;

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
}
