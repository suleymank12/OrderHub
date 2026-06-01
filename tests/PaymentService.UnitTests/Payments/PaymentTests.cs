using OrderHub.PaymentService.Domain.Payments;
using OrderHub.PaymentService.Domain.Payments.Events;
using OrderHub.PaymentService.Domain.Payments.Exceptions;
using OrderHub.PaymentService.UnitTests.TestData;

namespace OrderHub.PaymentService.UnitTests.Payments;

public sealed class PaymentTests
{
    [Fact]
    public void Create_ValidInput_ReturnsPendingPayment()
    {
        var orderId = Guid.NewGuid();

        var payment = Payment.Create(orderId, 100m, "TRY");

        payment.OrderId.Should().Be(orderId);
        payment.Amount.Should().Be(100m);
        payment.Currency.Should().Be("TRY");
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.ExternalTransactionId.Should().BeNull();
        payment.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_EmptyOrderId_ThrowsArgumentException()
    {
        var act = () => Payment.Create(Guid.Empty, 100m, "TRY");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Payment.Create(Guid.NewGuid(), -1m, "TRY");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkSucceeded_FromPending_SetsSucceededWithExternalTransactionId()
    {
        var payment = PaymentFactory.Pending();

        payment.MarkSucceeded("MOCK-TX-42");

        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.ExternalTransactionId.Should().Be("MOCK-TX-42");
    }

    [Fact]
    public void MarkSucceeded_FromPending_RaisesPaymentSucceeded()
    {
        var payment = PaymentFactory.Pending();

        payment.MarkSucceeded("MOCK-TX-42");

        var raised = payment.DomainEvents.OfType<PaymentSucceeded>().Should().ContainSingle().Which;
        raised.PaymentId.Should().Be(payment.Id);
        raised.OrderId.Should().Be(payment.OrderId);
        raised.ExternalTransactionId.Should().Be("MOCK-TX-42");
    }

    [Fact]
    public void MarkSucceeded_AlreadySucceeded_ThrowsInvalidTransition()
    {
        var payment = PaymentFactory.Succeeded();

        var act = () => payment.MarkSucceeded("MOCK-TX-2");

        var exception = act.Should().Throw<InvalidPaymentStatusTransitionException>().Which;
        exception.From.Should().Be(PaymentStatus.Succeeded);
        exception.To.Should().Be(PaymentStatus.Succeeded);
    }

    [Fact]
    public void MarkSucceeded_AlreadyFailed_ThrowsInvalidTransition()
    {
        var payment = PaymentFactory.Failed();

        var act = () => payment.MarkSucceeded("MOCK-TX-2");

        act.Should().Throw<InvalidPaymentStatusTransitionException>()
            .Which.From.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void MarkFailed_FromPending_SetsFailedAndRaisesPaymentFailed()
    {
        var payment = PaymentFactory.Pending();

        payment.MarkFailed("insufficient funds");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.ExternalTransactionId.Should().BeNull();
        var raised = payment.DomainEvents.OfType<PaymentFailed>().Should().ContainSingle().Which;
        raised.PaymentId.Should().Be(payment.Id);
        raised.OrderId.Should().Be(payment.OrderId);
        raised.Reason.Should().Be("insufficient funds");
    }

    [Fact]
    public void MarkFailed_AlreadyFinalized_ThrowsInvalidTransition()
    {
        var payment = PaymentFactory.Succeeded();

        var act = () => payment.MarkFailed("late failure");

        act.Should().Throw<InvalidPaymentStatusTransitionException>();
    }
}
