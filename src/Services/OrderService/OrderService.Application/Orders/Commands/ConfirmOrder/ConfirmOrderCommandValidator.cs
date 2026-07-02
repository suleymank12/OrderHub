using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Commands.ConfirmOrder;

/// <summary><see cref="ConfirmOrderCommand"/> input-contract doğrulaması; hata → 400 (ValidationBehavior).</summary>
public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    public ConfirmOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
