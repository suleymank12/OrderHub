using Mapster;
using OrderHub.AnalyticsService.Application.Orders.Dtos;
using OrderHub.AnalyticsService.Application.Revenue.Dtos;
using OrderHub.AnalyticsService.Domain.Orders;
using OrderHub.AnalyticsService.Domain.Revenue;

namespace OrderHub.AnalyticsService.Application.Mapping;

/// <summary>
/// Domain read-model → DTO map'lemelerini tanımlar (Mapster <see cref="IRegister"/>; DI'da <c>config.Scan</c>
/// ile keşfedilir). <b>Yalnızca outbound</b>: read-side'da inbound map'leme yoktur (projection'lar Kafka
/// consumer tarafından domain factory'leriyle üretilir).
/// </summary>
public sealed class AnalyticsMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // enum → string: API kontrat stabilitesi (sayısal değer değişse de client kırılmaz).
        config.NewConfig<OrderProjection, OrderProjectionDto>()
            .Map(dest => dest.Status, src => src.Status.ToString());

        config.NewConfig<DailyRevenueProjection, DailyRevenueDto>();
    }
}
