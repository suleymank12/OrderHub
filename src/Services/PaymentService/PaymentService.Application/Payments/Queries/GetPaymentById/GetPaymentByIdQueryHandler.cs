using MapsterMapper;
using MediatR;
using OrderHub.Common.Results;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Application.Payments.Dtos;

namespace OrderHub.PaymentService.Application.Payments.Queries.GetPaymentById;

/// <summary>
/// <see cref="GetPaymentByIdQuery"/> handler'ı. Ödeme bulunamazsa exception fırlatmaz; 404 beklenen sonuç
/// olduğundan <c>Result.Failure(Error.NotFound)</c> döner. Domain → DTO map'lemesi outbound → Mapster.
/// </summary>
internal sealed class GetPaymentByIdQueryHandler(
    IPaymentRepository repository,
    IMapper mapper)
    : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, cancellationToken);

        if (payment is null)
        {
            return Result.Failure<PaymentDto>(
                Error.NotFound("Payment.NotFound", $"Payment with id '{request.PaymentId}' was not found."));
        }

        return Result.Success(mapper.Map<PaymentDto>(payment));
    }
}
