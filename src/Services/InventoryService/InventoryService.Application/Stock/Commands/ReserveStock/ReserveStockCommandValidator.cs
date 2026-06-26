using FluentValidation;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReserveStock;

/// <summary>
/// <see cref="ReserveStockCommand"/> input-contract doğrulaması. Hata → ValidationBehavior
/// <c>ValidationException</c> fırlatır → 400. Domain factory'si de invariant doğrular; buradaki kurallar
/// fail-fast ve anlamlı hata mesajı içindir (savunma katmanı).
/// </summary>
public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0)
            .WithMessage("Reservation quantity must be positive.");
    }
}
