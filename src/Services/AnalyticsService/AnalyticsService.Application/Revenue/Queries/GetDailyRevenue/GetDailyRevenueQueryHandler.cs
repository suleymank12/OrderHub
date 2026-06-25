using MapsterMapper;
using MediatR;
using OrderHub.AnalyticsService.Application.Abstractions.Persistence;
using OrderHub.AnalyticsService.Application.Revenue.Dtos;
using OrderHub.Common.Results;

namespace OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;

/// <summary>
/// <see cref="GetDailyRevenueQuery"/> handler'ı. Aralıktaki günlük gelir satırlarını repository'den (read-only)
/// alır ve outbound DTO'lara map'ler. Boş aralık başarılı bir sonuçtur (boş liste); liste sorgusu pratikte fail
/// olmaz ama uniform pipeline için <see cref="Result"/>'ta döner. Aralık tutarlılığı (From &lt;= To) validator'da.
/// </summary>
internal sealed class GetDailyRevenueQueryHandler(
    IAnalyticsReadRepository repository,
    IMapper mapper)
    : IRequestHandler<GetDailyRevenueQuery, Result<IReadOnlyList<DailyRevenueDto>>>
{
    public async Task<Result<IReadOnlyList<DailyRevenueDto>>> Handle(
        GetDailyRevenueQuery request,
        CancellationToken cancellationToken)
    {
        var projections = await repository.GetDailyRevenueAsync(request.From, request.To, cancellationToken);

        IReadOnlyList<DailyRevenueDto> items = projections
            .Select(mapper.Map<DailyRevenueDto>)
            .ToList();

        return Result.Success(items);
    }
}
