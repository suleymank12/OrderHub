using FluentValidation;

namespace OrderHub.InventoryService.Application.Stock.Commands.ConfirmStockReservation;

/// <summary>
/// <see cref="ConfirmStockReservationCommand"/> input-contract doğrulaması. Hata → ValidationBehavior
/// <c>ValidationException</c> fırlatır → 400.
/// </summary>
public sealed class ConfirmStockReservationCommandValidator : AbstractValidator<ConfirmStockReservationCommand>
{
    public ConfirmStockReservationCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.ProductId).NotEmpty();
    }
}
