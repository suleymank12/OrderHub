using OrderHub.PaymentService.Application.Abstractions.Messaging;
using OrderHub.PaymentService.Application.Payments.Dtos;

namespace OrderHub.PaymentService.Application.Payments.Queries.GetPaymentById;

/// <summary>Id ile tek bir ödemeyi getirir. Bulunamazsa handler <c>Result.Failure(NotFound)</c> döner (404).</summary>
/// <param name="PaymentId">Ödeme kimliği.</param>
public sealed record GetPaymentByIdQuery(Guid PaymentId) : IQuery<PaymentDto>;
