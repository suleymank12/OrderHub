using MapsterMapper;
using MediatR;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Pagination;
using OrderHub.OrderService.Application.Orders.Dtos;

namespace OrderHub.OrderService.Application.Orders.Queries.ListOrders;

/// <summary>
/// <see cref="ListOrdersQuery"/> handler'ı. Repository'den sayfalı veriyi (öğeler + toplam sayı) alır,
/// outbound DTO'lara map'ler ve <see cref="PagedResult{T}"/>'e sarar. Boş liste başarılı bir sonuçtur;
/// liste sorgusu pratikte fail olmaz ama uniform pipeline için <see cref="Result"/>'ta döner (Karar 4).
/// </summary>
internal sealed class ListOrdersQueryHandler(
    IOrderRepository repository,
    IMapper mapper)
    : IRequestHandler<ListOrdersQuery, Result<PagedResult<OrderDto>>>
{
    public async Task<Result<PagedResult<OrderDto>>> Handle(
        ListOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var (orders, totalCount) = await repository.GetPagedAsync(
            request.Page, request.PageSize, cancellationToken);

        var items = orders.Select(mapper.Map<OrderDto>).ToList();

        var pagedResult = new PagedResult<OrderDto>(items, request.Page, request.PageSize, totalCount);

        return Result.Success(pagedResult);
    }
}
