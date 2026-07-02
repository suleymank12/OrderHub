using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Commands.CancelOrder;

/// <summary><see cref="CancelOrderCommand"/> input-contract doğrulaması; hata → 400 (ValidationBehavior).</summary>
public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty();
    }
}
