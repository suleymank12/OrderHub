using FluentValidation;

namespace OrderHub.InventoryService.Application.Stock.Commands.ReleaseStock;

/// <summary>
/// <see cref="ReleaseStockCommand"/> input-contract doğrulaması. Hata → ValidationBehavior
/// <c>ValidationException</c> fırlatır → 400.
/// </summary>
public sealed class ReleaseStockCommandValidator : AbstractValidator<ReleaseStockCommand>
{
    public ReleaseStockCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.ProductId).NotEmpty();
    }
}
