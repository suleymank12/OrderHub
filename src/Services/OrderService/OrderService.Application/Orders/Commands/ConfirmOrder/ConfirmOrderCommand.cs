using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.ConfirmOrder;

/// <summary>
/// Bir siparişi onaylar (Pending → Confirmed). Faz 5'te <c>OrderProcessingSaga</c>, stok rezervasyonu başarılı
/// olunca (<c>StockReserved</c>) bunu command-consumer (5d-5) üzerinden <c>ISender</c> ile tetikler; HTTP ile değil.
/// Sonuç <c>bool</c>: geçiş uygulandıysa <c>true</c>, idempotent/edge no-op ise <c>false</c>.
/// </summary>
/// <param name="OrderId">Onaylanacak siparişin kimliği.</param>
public sealed record ConfirmOrderCommand(Guid OrderId) : ICommand<bool>;
