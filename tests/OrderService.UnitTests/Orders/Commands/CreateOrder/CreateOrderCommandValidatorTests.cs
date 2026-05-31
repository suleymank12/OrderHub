using FluentValidation.TestHelper;
using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.Application.ValueObjects.Dtos;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.CreateOrder;

public sealed class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        _validator.TestValidate(CreateOrderRequestFactory.ValidCommand())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullShippingAddress_HasError()
    {
        var command = CreateOrderRequestFactory.ValidCommand() with { ShippingAddress = null! };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.ShippingAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyAddressStreet_HasError(string street)
    {
        var command = CreateOrderRequestFactory.ValidCommand() with
        {
            ShippingAddress = new AddressDto(street, "Istanbul", "34000", "Türkiye")
        };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.ShippingAddress.Street);
    }

    [Fact]
    public void Validate_EmptyItems_HasError()
    {
        var command = CreateOrderRequestFactory.ValidCommand([]);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Items);
    }

    [Fact]
    public void Validate_MoreThan100Items_HasError()
    {
        var items = Enumerable.Range(0, 101).Select(_ => CreateOrderRequestFactory.Item()).ToList();
        var command = CreateOrderRequestFactory.ValidCommand(items);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ItemQuantityNotPositive_HasError(int quantity)
    {
        var command = CreateOrderRequestFactory.ValidCommand(
            [new CreateOrderItemRequest(Guid.NewGuid(), quantity, CreateOrderRequestFactory.Money())]);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_ItemNegativeUnitPrice_HasError()
    {
        var command = CreateOrderRequestFactory.ValidCommand(
            [new CreateOrderItemRequest(Guid.NewGuid(), 1, new MoneyDto(-5m, "TRY"))]);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor("Items[0].UnitPrice.Amount");
    }

    [Fact]
    public void Validate_ItemUnsupportedCurrency_HasError()
    {
        var command = CreateOrderRequestFactory.ValidCommand(
            [new CreateOrderItemRequest(Guid.NewGuid(), 1, new MoneyDto(100m, "XXX"))]);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor("Items[0].UnitPrice.Currency");
    }

    [Fact]
    public void Validate_ItemEmptyProductId_HasError()
    {
        var command = CreateOrderRequestFactory.ValidCommand(
            [new CreateOrderItemRequest(Guid.Empty, 1, CreateOrderRequestFactory.Money())]);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor("Items[0].ProductId");
    }
}
