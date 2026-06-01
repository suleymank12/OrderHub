using OrderHub.Common.Results;

namespace OrderHub.Common.UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResultWithNoneError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_CreatesFailedResultWithError()
    {
        var error = Error.Failure("Code", "message");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void IsFailure_IsInverseOfIsSuccess()
    {
        Result.Success().IsFailure.Should().BeFalse();
        Result.Failure(Error.Failure("Code", "message")).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SuccessOfT_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_IsFailureWithError()
    {
        var error = Error.NotFound("Code", "message");

        var result = Result.Failure<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Value_OnFailure_ThrowsInvalidOperationException()
    {
        var result = Result.Failure<int>(Error.NotFound("Code", "message"));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_WithNoneError_ThrowsInvalidOperationException()
    {
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_SuccessWithError_ThrowsInvalidOperationException()
    {
        var act = () => new TestableResult(isSuccess: true, Error.Failure("Code", "message"));

        act.Should().Throw<InvalidOperationException>();
    }
}

/// <summary>Korumalı ctor invariant'ını test etmek için minimal <see cref="Result"/> türevi.</summary>
file sealed class TestableResult : Result
{
    public TestableResult(bool isSuccess, Error error)
        : base(isSuccess, error)
    {
    }
}
