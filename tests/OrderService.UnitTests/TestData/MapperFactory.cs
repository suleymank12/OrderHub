using Mapster;
using MapsterMapper;
using OrderHub.OrderService.Application.Mapping;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>
/// Query handler testleri için <b>gerçek</b> <see cref="IMapper"/> üretir (mock değil): mapping'i
/// mock'lamak coverage gaming olurdu ve mapping bug'ları sessizce geçerdi. Bu mapper, prod ile aynı
/// <see cref="OrderMappingConfig"/>'i kullanır → handler testleri aynı zamanda mapping'i de doğrular.
/// </summary>
internal static class MapperFactory
{
    public static IMapper Create()
    {
        var config = new TypeAdapterConfig();
        new OrderMappingConfig().Register(config);
        return new Mapper(config);
    }
}
