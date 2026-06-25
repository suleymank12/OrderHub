using MapsterMapper;
using MediatR;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Application.Orders.Dtos;
using OrderHub.Common.Results;

namespace OrderHub.AnalyticsService.Application.Orders.Queries.GetOrderProjectionById;

/// <summary>
/// <see cref="GetOrderProjectionByIdQuery"/> handler'ı. Projection bulunamazsa exception fırlatmaz; 404 beklenen
/// sonuç olduğundan <c>Result.Failure(Error.NotFound)</c> döner. Domain read-model → DTO map'lemesi outbound →
/// Mapster (enum status → string).
/// </summary>
internal sealed class GetOrderProjectionByIdQueryHandler(
    IAnalyticsReadRepository repository,
    IMapper mapper)
    : IRequestHandler<GetOrderProjectionByIdQuery, Result<OrderProjectionDto>>
{
    public async Task<Result<OrderProjectionDto>> Handle(
        GetOrderProjectionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var projection = await repository.GetOrderProjectionByIdAsync(request.OrderId, cancellationToken);

        if (projection is null)
        {
            return Result.Failure<OrderProjectionDto>(
                Error.NotFound(
                    "OrderProjection.NotFound",
                    $"Order projection with id '{request.OrderId}' was not found."));
        }

        return Result.Success(mapper.Map<OrderProjectionDto>(projection));
    }
}
