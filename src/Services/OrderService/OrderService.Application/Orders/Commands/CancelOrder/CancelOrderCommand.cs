using OrderHub.OrderService.Application.Abstractions.Messaging;

namespace OrderHub.OrderService.Application.Orders.Commands.CancelOrder;

/// <summary>
/// Bir siparişi iptal eder (Pending/Confirmed → Cancelled). Faz 5'te <c>OrderProcessingSaga</c>, telafi dalında
/// (stok ayrılamadı / ödeme reddedildi) bunu command-consumer (5e-1) üzerinden <c>ISender</c> ile tetikler; HTTP
/// ile değil. Gerekçe saga'dan gelir (<c>stock_unavailable</c> / <c>payment_failed</c>). Sonuç <c>bool</c>: iptal
/// uygulandıysa <c>true</c>, idempotent no-op (zaten Cancelled) ise <c>false</c>.
/// </summary>
/// <param name="OrderId">İptal edilecek siparişin kimliği.</param>
/// <param name="Reason">İptal gerekçesi (saga telafi dalı).</param>
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand<bool>;
