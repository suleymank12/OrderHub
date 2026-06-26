using Mapster;
using MapsterMapper;
using OrderHub.AnalyticsService.Application.Mapping;

namespace OrderHub.AnalyticsService.UnitTests.TestData;

/// <summary>
/// Query handler testleri için <b>gerçek</b> <see cref="IMapper"/> üretir (mock değil): mapping'i mock'lamak
/// coverage gaming olurdu ve mapping bug'ları sessizce geçerdi. Bu mapper prod ile aynı
/// <see cref="AnalyticsMappingConfig"/>'i kullanır → handler testleri aynı zamanda mapping'i de doğrular.
/// </summary>
internal static class MapperFactory
{
    public static IMapper Create()
    {
        var config = new TypeAdapterConfig();
        new AnalyticsMappingConfig().Register(config);
        return new Mapper(config);
    }
}
