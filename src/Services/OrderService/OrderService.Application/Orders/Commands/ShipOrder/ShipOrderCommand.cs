using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.ShipOrder;

/// <summary>
/// Bir siparişi kargolanmış işaretler (Paid → Shipped). Faz 5'te <c>OrderProcessingSaga</c>, stok rezervasyonu
/// onaylanınca (<c>StockReservationConfirmed</c>) bunu command-consumer (5d-5) üzerinden <c>ISender</c> ile
/// tetikler. Sonuç <c>bool</c>: geçiş uygulandıysa <c>true</c>, idempotent/edge no-op ise <c>false</c>.
/// </summary>
/// <param name="OrderId">Kargolanmış işaretlenecek siparişin kimliği.</param>
public sealed record ShipOrderCommand(Guid OrderId) : ICommand<bool>;
