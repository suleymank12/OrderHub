using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderHub.PaymentService.Api.Common;
using OrderHub.PaymentService.Application.Payments.Dtos;
using OrderHub.PaymentService.Application.Payments.Queries.GetPaymentById;

namespace OrderHub.PaymentService.Api.Controllers;

/// <summary>
/// Ödeme sorgu uçları. <see cref="AuthorizeAttribute"/> ile korumalı. <b>POST endpoint yoktur</b> — ödeme
/// dışarıdan command-event (RabbitMQ consumer, 3c) ile tetiklenir, HTTP ile değil. Başarısız
/// <see cref="OrderHub.Common.Results.Result"/>'lar ProblemDetails'e map'lenir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payments")]
public sealed class PaymentsController(ISender sender) : ControllerBase
{
    /// <summary>Id ile ödemeyi (durumuyla) getirir.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPaymentByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblemResult();
    }
}
