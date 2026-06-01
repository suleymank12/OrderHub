using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Common.Pagination;
using OrderHub.OrderService.Application.Orders.Dtos;

namespace OrderHub.OrderService.Application.Orders.Queries.ListOrders;

/// <summary>
/// Sayfalı sipariş listesi sorgusu. <see cref="Page"/>/<see cref="PageSize"/> aralıkları
/// <c>ListOrdersQueryValidator</c> ile sınırlanır (sınırsız <c>PageSize</c> = DoS/perf riski, K5).
/// </summary>
/// <param name="Page">1-tabanlı sayfa numarası (varsayılan 1).</param>
/// <param name="PageSize">Sayfa başına öğe sayısı (varsayılan 20).</param>
public sealed record ListOrdersQuery(int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<OrderDto>>;
