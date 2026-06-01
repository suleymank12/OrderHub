using MapsterMapper;
using MediatR;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Dtos;

namespace OrderHub.OrderService.Application.Orders.Queries.GetOrderById;

/// <summary>
/// <see cref="GetOrderByIdQuery"/> handler'ı. Sipariş bulunamazsa exception fırlatmaz; 404 beklenen bir
/// sonuç olduğundan <c>Result.Failure(Error.NotFound)</c> döner (Karar 4). Domain → DTO map'lemesi
/// outbound olduğu için Mapster ile yapılır.
/// </summary>
internal sealed class GetOrderByIdQueryHandler(
    IOrderRepository repository,
    IMapper mapper)
    : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDto>(
                Error.NotFound("Order.NotFound", $"Order with id '{request.OrderId}' was not found."));
        }

        return Result.Success(mapper.Map<OrderDto>(order));
    }
}
