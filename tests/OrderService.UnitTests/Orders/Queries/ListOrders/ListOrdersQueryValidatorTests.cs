using FluentValidation.TestHelper;
using OrderHub.OrderService.Application.Orders.Queries.ListOrders;

namespace OrderHub.OrderService.UnitTests.Orders.Queries.ListOrders;

public sealed class ListOrdersQueryValidatorTests
{
    private readonly ListOrdersQueryValidator _validator = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 20)]
    [InlineData(5, 100)]
    public void Validate_ValidPaging_HasNoErrors(int page, int pageSize)
    {
        _validator.TestValidate(new ListOrdersQuery(page, pageSize))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageLessThanOne_HasError(int page)
    {
        _validator.TestValidate(new ListOrdersQuery(page, 20))
            .ShouldHaveValidationErrorFor(query => query.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(1000)]
    public void Validate_PageSizeOutOfRange_HasError(int pageSize)
    {
        _validator.TestValidate(new ListOrdersQuery(1, pageSize))
            .ShouldHaveValidationErrorFor(query => query.PageSize);
    }
}
