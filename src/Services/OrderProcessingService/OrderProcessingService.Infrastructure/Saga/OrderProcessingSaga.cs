using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.Infrastructure.Saga;

/// <summary>
/// OrderProcessingSaga — sipariş işleme orkestrasyonu (ADR-0007 Karar 4). <b>5d-4a iskeleti:</b> durumlar,
/// event'ler ve correlation (hepsi <c>OrderId</c> üzerinden) tanımlıdır; <b>transition gövdeleri 5d-4b'de</b>
/// eklenecek (henüz <c>Initially</c>/<c>During</c> davranışı yok → saga pasif, mevcut akış etkilenmez).
/// <para>
/// Hedef happy-path (5d-4b): OrderPlaced → N× ReserveStock → (hepsi reserved) ConfirmOrder + ProcessPayment →
/// PaymentSucceeded → N× ConfirmStockReservation + MarkOrderPaid → (hepsi confirmed) ShipOrder → Completed.
/// Fan-out sayımı <see cref="OrderProcessingSagaState"/> kümeleriyle (Karar B), concurrency RowVersion + retry ile.
/// </para>
/// </summary>
internal sealed class OrderProcessingSaga : MassTransitStateMachine<OrderProcessingSagaState>
{
    public OrderProcessingSaga()
    {
        InstanceState(instance => instance.CurrentState);

        // Tüm olaylar OrderId ile correlate olur (saga örneği = sipariş). Transition gövdeleri 5d-4b.
        Event(() => OrderPlaced, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReserved, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(context => context.Message.OrderId));
        Event(() => StockReservationConfirmed, x => x.CorrelateById(context => context.Message.OrderId));
    }

    /// <summary>Tüm kalemlerin stok rezervasyonu beklenir (OrderPlaced sonrası).</summary>
    public State AwaitingStockReservation { get; private set; } = null!;

    /// <summary>Ödeme sonucu beklenir (rezervasyon tamam + ProcessPayment gönderildikten sonra).</summary>
    public State AwaitingPayment { get; private set; } = null!;

    /// <summary>Tüm kalemlerin rezervasyon onayı beklenir (ödeme başarılı sonrası).</summary>
    public State AwaitingStockConfirmation { get; private set; } = null!;

    /// <summary>Saga tetik olayı (OrderService → saga): sipariş yerleşti.</summary>
    public Event<OrderPlacedIntegrationEvent> OrderPlaced { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün stoğu ayrıldı (fan-in).</summary>
    public Event<StockReservedIntegrationEvent> StockReserved { get; private set; } = null!;

    /// <summary>PaymentService → saga: ödeme başarıyla tamamlandı.</summary>
    public Event<PaymentSucceededIntegrationEvent> PaymentSucceeded { get; private set; } = null!;

    /// <summary>InventoryService → saga: bir ürünün rezervasyonu onaylandı (fan-in).</summary>
    public Event<StockReservationConfirmedIntegrationEvent> StockReservationConfirmed { get; private set; } = null!;
}
