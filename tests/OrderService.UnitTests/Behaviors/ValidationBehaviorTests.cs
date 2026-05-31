using FluentValidation;
using FluentValidation.Results;
using Moq;
using OrderHub.OrderService.Application.Behaviors;

namespace OrderHub.OrderService.UnitTests.Behaviors;

/// <summary>Moq'un (Castle DynamicProxy) <c>IValidator&lt;SampleRequest&gt;</c>'i proxy'leyebilmesi için public.</summary>
public sealed record SampleRequest(string Value);

public sealed class ValidationBehaviorTests
{
    private const string NextResponse = "handler-result";

    [Fact]
    public async Task Handle_NoValidators_InvokesNextAndReturnsResponse()
    {
        var sut = new ValidationBehavior<SampleRequest, string>([]);

        var result = await sut.Handle(new SampleRequest("x"), () => Task.FromResult(NextResponse), CancellationToken.None);

        result.Should().Be(NextResponse);
    }

    [Fact]
    public async Task Handle_AllValidatorsPass_InvokesNext()
    {
        var sut = new ValidationBehavior<SampleRequest, string>([PassingValidator(), PassingValidator()]);

        var result = await sut.Handle(new SampleRequest("x"), () => Task.FromResult(NextResponse), CancellationToken.None);

        result.Should().Be(NextResponse);
    }

    [Fact]
    public async Task Handle_ValidatorFails_ThrowsValidationException()
    {
        var sut = new ValidationBehavior<SampleRequest, string>([FailingValidator("Value", "required")]);

        var act = () => sut.Handle(new SampleRequest("x"), () => Task.FromResult(NextResponse), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ValidatorFails_DoesNotInvokeNext()
    {
        var sut = new ValidationBehavior<SampleRequest, string>([FailingValidator("Value", "required")]);
        var invoked = false;

        var act = () => sut.Handle(
            new SampleRequest("x"),
            () =>
            {
                invoked = true;
                return Task.FromResult(NextResponse);
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MultipleFailingValidators_AggregatesAllFailures()
    {
        var sut = new ValidationBehavior<SampleRequest, string>(
            [FailingValidator("Value", "error-1"), FailingValidator("Other", "error-2")]);

        var act = () => sut.Handle(new SampleRequest("x"), () => Task.FromResult(NextResponse), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().HaveCount(2);
    }

    private static IValidator<SampleRequest> PassingValidator()
    {
        var validator = new Mock<IValidator<SampleRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<SampleRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return validator.Object;
    }

    private static IValidator<SampleRequest> FailingValidator(string property, string message)
    {
        var validator = new Mock<IValidator<SampleRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<SampleRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure(property, message)]));
        return validator.Object;
    }
}
