using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderHub.OrderService.Api.Common;
using OrderHub.OrderService.Application.Common.Pagination;
using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.Application.Orders.Dtos;
using OrderHub.OrderService.Application.Orders.Queries.GetOrderById;
using OrderHub.OrderService.Application.Orders.Queries.ListOrders;

namespace OrderHub.OrderService.Api.Controllers;

/// <summary>
/// Sipariş uçları. Tümü <see cref="AuthorizeAttribute"/> ile korumalı (authenticated). Müşteri kimliği
/// token'dan türetilir (handler içinde <c>ICurrentUserService</c>), request body'sinden değil (K3 IDOR).
/// Başarısız <see cref="OrderHub.Common.Results.Result"/>'lar ProblemDetails'e map'lenir; invariant/validation
/// exception'ları GlobalExceptionHandler ele alır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    /// <summary>Yeni sipariş oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(request.ShippingAddress, request.Items);
        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value })
            : result.ToProblemResult();
    }

    /// <summary>Id ile siparişi getirir.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrderByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblemResult();
    }

    /// <summary>Siparişleri sayfalı listeler.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ListOrdersQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblemResult();
    }
}
