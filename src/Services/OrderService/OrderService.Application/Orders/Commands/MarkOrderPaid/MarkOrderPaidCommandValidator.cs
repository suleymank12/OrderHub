using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Commands.MarkOrderPaid;

/// <summary><see cref="MarkOrderPaidCommand"/> input-contract doğrulaması; hata → 400 (ValidationBehavior).</summary>
public sealed class MarkOrderPaidCommandValidator : AbstractValidator<MarkOrderPaidCommand>
{
    public MarkOrderPaidCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
