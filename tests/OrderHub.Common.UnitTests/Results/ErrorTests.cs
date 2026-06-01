using OrderHub.Common.Results;

namespace OrderHub.Common.UnitTests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void None_HasEmptyCodeAndMessage()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Message.Should().BeEmpty();
    }

    [Fact]
    public void Validation_SetsValidationTypeWithCodeAndMessage()
    {
        var error = Error.Validation("Order.Invalid", "invalid order");

        error.Code.Should().Be("Order.Invalid");
        error.Message.Should().Be("invalid order");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void NotFound_SetsNotFoundType()
    {
        Error.NotFound("Order.NotFound", "missing").Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Conflict_SetsConflictType()
    {
        Error.Conflict("Order.AlreadyPaid", "conflict").Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Failure_SetsFailureType()
    {
        Error.Failure("Order.Failure", "failure").Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var first = Error.NotFound("Code", "message");
        var second = Error.NotFound("Code", "message");

        first.Should().Be(second);
    }

    [Fact]
    public void Equality_DifferentType_AreNotEqual()
    {
        var validation = Error.Validation("Code", "message");
        var notFound = Error.NotFound("Code", "message");

        validation.Should().NotBe(notFound);
    }
}
