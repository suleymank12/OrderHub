using Mapster;
using OrderHub.OrderService.Application.Orders.Dtos;
using OrderHub.OrderService.Application.ValueObjects.Dtos;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Application.Mapping;

/// <summary>
/// Domain entity/VO → DTO map'lemelerini tanımlar (Mapster <see cref="IRegister"/>; DI'da
/// <c>config.Scan</c> ile keşfedilir). <b>Yalnızca outbound</b>: inbound (DTO → domain) ASLA Mapster ile
/// yapılmaz — <c>Money.Create</c>/<c>Address.Create</c>/<c>OrderItem.Create</c> factory'leri invariant
/// doğrular; Mapster bunları bypass ederse domain kuralları delinir (handler'da explicit map'lenir).
/// </summary>
public sealed class OrderMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // enum → string: API kontrat stabilitesi (sayısal değer değişse de client kırılmaz).
        config.NewConfig<Money, MoneyDto>()
            .Map(dest => dest.Currency, src => src.Currency.ToString());

        config.NewConfig<Address, AddressDto>();

        // UnitPrice ve (türetilmiş) Subtotal, Money→MoneyDto kuralı üzerinden map'lenir.
        config.NewConfig<OrderItem, OrderItemDto>();

        config.NewConfig<Order, OrderDto>()
            .Map(dest => dest.Status, src => src.Status.ToString());
    }
}
