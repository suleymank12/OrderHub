using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderHub.AnalyticsService.Api.Common;
using OrderHub.AnalyticsService.Application.Orders.Dtos;
using OrderHub.AnalyticsService.Application.Orders.Queries.GetOrderProjectionById;
using OrderHub.AnalyticsService.Application.Revenue.Dtos;
using OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;

namespace OrderHub.AnalyticsService.Api.Controllers;

/// <summary>
/// Analytics read-model sorgu uçları (CQRS read-side, ADR-0006). <see cref="AuthorizeAttribute"/> ile korumalı
/// (JWT). <b>Write endpoint yoktur</b> — projection'lar yalnız Kafka order-stream consumer ile güncellenir.
/// Başarısız <see cref="OrderHub.Common.Results.Result"/>'lar ProblemDetails'e map'lenir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/analytics")]
public sealed class AnalyticsController(ISender sender) : ControllerBase
{
    /// <summary>Id ile sipariş read-model'ini (durumuyla) getirir.</summary>
    [HttpGet("orders/{id:guid}")]
    [ProducesResponseType(typeof(OrderProjectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderProjectionById(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrderProjectionByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblemResult();
    }

    /// <summary>Bir tarih aralığındaki (her ikisi dahil) günlük gelir satırlarını getirir.</summary>
    [HttpGet("revenue/daily")]
    [ProducesResponseType(typeof(IReadOnlyList<DailyRevenueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDailyRevenue(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDailyRevenueQuery(from, to), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblemResult();
    }
}
