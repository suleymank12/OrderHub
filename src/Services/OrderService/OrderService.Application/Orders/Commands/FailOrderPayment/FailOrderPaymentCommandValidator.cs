using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Commands.FailOrderPayment;

/// <summary><see cref="FailOrderPaymentCommand"/> input-contract doğrulaması; hata → 400 (ValidationBehavior).</summary>
public sealed class FailOrderPaymentCommandValidator : AbstractValidator<FailOrderPaymentCommand>
{
    public FailOrderPaymentCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
