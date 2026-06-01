using Mapster;
using OrderHub.PaymentService.Application.Payments.Dtos;
using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.Application.Mapping;

/// <summary>
/// Domain → DTO map'lemelerini tanımlar (Mapster <see cref="IRegister"/>; DI'da <c>config.Scan</c> ile
/// keşfedilir). <b>Yalnızca outbound</b>: inbound map'leme domain factory'leriyle yapılır (invariant koruması).
/// </summary>
public sealed class PaymentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // enum → string: API kontrat stabilitesi (sayısal değer değişse de client kırılmaz).
        config.NewConfig<Payment, PaymentDto>()
            .Map(dest => dest.Status, src => src.Status.ToString());
    }
}
