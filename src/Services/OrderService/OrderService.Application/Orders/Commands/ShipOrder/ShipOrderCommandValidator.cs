using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Commands.ShipOrder;

/// <summary><see cref="ShipOrderCommand"/> input-contract doğrulaması; hata → 400 (ValidationBehavior).</summary>
public sealed class ShipOrderCommandValidator : AbstractValidator<ShipOrderCommand>
{
    public ShipOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
