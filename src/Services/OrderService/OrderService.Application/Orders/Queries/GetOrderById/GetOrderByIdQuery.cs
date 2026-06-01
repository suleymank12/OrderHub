using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Orders.Dtos;

namespace OrderHub.OrderService.Application.Orders.Queries.GetOrderById;

/// <summary>Id ile tek bir siparişi getirir. Bulunamazsa handler <c>Result.Failure(NotFound)</c> döner (404).</summary>
/// <param name="OrderId">Sipariş kimliği.</param>
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;
