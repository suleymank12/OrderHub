using OrderHub.Common.Exceptions;

namespace OrderHub.Common.UnitTests.Exceptions;

public sealed class DomainExceptionTests
{
    [Fact]
    public void MessageConstructor_SetsMessage()
    {
        var exception = new TestDomainException("something broke");

        exception.Message.Should().Be("something broke");
    }

    [Fact]
    public void InnerExceptionConstructor_SetsMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new TestDomainException("outer", inner);

        exception.Message.Should().Be("outer");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Derives_FromSystemException()
    {
        new TestDomainException("x").Should().BeAssignableTo<Exception>();
    }
}

/// <summary>Soyut <see cref="DomainException"/>'ı test etmek için minimal türev.</summary>
file sealed class TestDomainException : DomainException
{
    public TestDomainException(string message)
        : base(message)
    {
    }

    public TestDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
