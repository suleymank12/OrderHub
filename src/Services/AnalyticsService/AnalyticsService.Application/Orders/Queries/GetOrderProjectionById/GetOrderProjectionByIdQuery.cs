using OrderHub.AnalyticsService.Application.Abstractions.Messaging;
using OrderHub.AnalyticsService.Application.Orders.Dtos;

namespace OrderHub.AnalyticsService.Application.Orders.Queries.GetOrderProjectionById;

/// <summary>
/// Id ile tek bir sipariş read-model'ini getirir. Bulunamazsa handler <c>Result.Failure(NotFound)</c> döner (404).
/// </summary>
/// <param name="OrderId">Sipariş kimliği.</param>
public sealed record GetOrderProjectionByIdQuery(Guid OrderId) : IQuery<OrderProjectionDto>;
