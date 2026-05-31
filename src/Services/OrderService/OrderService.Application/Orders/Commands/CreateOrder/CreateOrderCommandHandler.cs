using MediatR;
using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Identity;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Application.Orders.Commands.CreateOrder;

/// <summary>
/// <see cref="CreateOrderCommand"/> handler'ı: müşteri kimliğini auth context'ten alır, inbound DTO'yu
/// domain factory'leriyle (Mapster ile DEĞİL → invariant'lar korunur, Karar 3/B) Order aggregate'ine
/// çevirir ve repository'ye ekler. <c>SaveChanges</c>'i çağırmaz — commit'i TransactionBehavior yönetir
/// (tek commit boundary, Karar 2c). Id client-side üretildiği için commit öncesi döndürülebilir.
/// Metot async değildir: I/O yoktur (repository.Add sync), bu yüzden <see cref="Task.FromResult{TResult}"/>.
/// </summary>
internal sealed class CreateOrderCommandHandler(
    IOrderRepository repository,
    ICurrentUserService currentUser,
    ILogger<CreateOrderCommandHandler> logger)
    : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Müşteri kimliği daima auth context'ten; asla request body'sinden (IDOR/mass-assignment, K3).
        var customerId = currentUser.UserId;

        var shippingAddress = Address.Create(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.PostalCode,
            request.ShippingAddress.Country);

        var items = request.Items
            .Select(item => OrderItem.Create(
                item.ProductId,
                item.Quantity,
                Money.Create(item.UnitPrice.Amount, ParseCurrency(item.UnitPrice.Currency))))
            .ToList();

        var order = Order.Create(customerId, shippingAddress, items);

        repository.Add(order);

        ApplicationLog.OrderCreated(logger, order.Id, customerId);

        return Task.FromResult(Result.Success(order.Id));
    }

    // Validator currency'nin parse edilebilirliğini garanti eder; burada parse güvenlidir.
    private static Currency ParseCurrency(string currency) => Enum.Parse<Currency>(currency, ignoreCase: true);
}
