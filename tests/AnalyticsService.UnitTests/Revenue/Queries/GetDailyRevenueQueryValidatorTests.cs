using FluentValidation.TestHelper;
using OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;

namespace OrderHub.AnalyticsService.UnitTests.Revenue.Queries;

public sealed class GetDailyRevenueQueryValidatorTests
{
    private readonly GetDailyRevenueQueryValidator _validator = new();

    [Fact]
    public void Validate_FromBeforeTo_HasNoErrors()
    {
        _validator.TestValidate(new GetDailyRevenueQuery(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FromEqualsTo_HasNoErrors()
    {
        var day = new DateOnly(2026, 6, 15);
        _validator.TestValidate(new GetDailyRevenueQuery(day, day))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FromAfterTo_HasError()
    {
        _validator.TestValidate(new GetDailyRevenueQuery(new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 1)))
            .ShouldHaveValidationErrorFor(query => query.To);
    }
}
