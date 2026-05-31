using FluentValidation;
using OrderHub.OrderService.Application.ValueObjects.Dtos;
using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.Application.Orders.Commands.CreateOrder;

/// <summary>
/// <see cref="CreateOrderCommand"/> input-contract doğrulaması. Hata → ValidationBehavior
/// <c>ValidationException</c> fırlatır → 400. Domain factory'leri de invariant doğrular; buradaki
/// kurallar fail-fast ve anlamlı hata mesajı içindir (savunma katmanı, çelişki değil).
/// </summary>
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    private const int MaxItemsPerOrder = 100;

    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.ShippingAddress)
            .NotNull()
            .SetValidator(new AddressDtoValidator());

        RuleFor(command => command.Items)
            .NotEmpty().WithMessage("An order must contain at least one item.")
            .Must(items => items.Count <= MaxItemsPerOrder)
            .WithMessage($"An order cannot contain more than {MaxItemsPerOrder} items.");

        RuleForEach(command => command.Items).SetValidator(new CreateOrderItemRequestValidator());
    }
}

/// <summary>Inbound sipariş kalemi doğrulaması (ürün, adet, birim fiyat).</summary>
internal sealed class CreateOrderItemRequestValidator : AbstractValidator<CreateOrderItemRequest>
{
    public CreateOrderItemRequestValidator()
    {
        RuleFor(item => item.ProductId).NotEmpty();
        RuleFor(item => item.Quantity).GreaterThan(0);

        RuleFor(item => item.UnitPrice).NotNull();
        RuleFor(item => item.UnitPrice.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative.");
        RuleFor(item => item.UnitPrice.Currency)
            .NotEmpty()
            .Must(currency => Enum.TryParse<Currency>(currency, ignoreCase: true, out _))
            .WithMessage("Unsupported currency.");
    }
}

/// <summary>Teslimat adresi DTO doğrulaması; tüm alanlar zorunlu ve sınırlı uzunlukta.</summary>
internal sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(address => address.Street).NotEmpty().MaximumLength(200);
        RuleFor(address => address.City).NotEmpty().MaximumLength(100);
        RuleFor(address => address.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(address => address.Country).NotEmpty().MaximumLength(100);
    }
}
