using FluentValidation;

namespace OrderHub.PaymentService.Application.Payments.Commands.ProcessPayment;

/// <summary>
/// <see cref="ProcessPaymentCommand"/> input-contract doğrulaması. Hata → ValidationBehavior
/// <c>ValidationException</c> fırlatır → 400. Domain factory'si de invariant doğrular; buradaki kurallar
/// fail-fast ve anlamlı hata mesajı içindir (savunma katmanı).
/// </summary>
public sealed class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Amount).GreaterThan(0).WithMessage("Payment amount must be positive.");
        RuleFor(command => command.Currency)
            .NotEmpty()
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
