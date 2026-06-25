using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.PaymentService.Application.Abstractions.Payments;
using OrderHub.PaymentService.Application.Abstractions.Persistence;
using OrderHub.PaymentService.Application.Payments.Commands.ProcessPayment;
using OrderHub.PaymentService.Domain.Payments;

namespace OrderHub.PaymentService.UnitTests.Payments.Commands.ProcessPayment;

public sealed class ProcessPaymentCommandHandlerTests
{
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentProcessor> _processor = new();
    private readonly ProcessPaymentCommandHandler _sut;

    public ProcessPaymentCommandHandlerTests()
    {
        _sut = new ProcessPaymentCommandHandler(
            _repository.Object,
            _processor.Object,
            NullLogger<ProcessPaymentCommandHandler>.Instance);
    }

    private static ProcessPaymentCommand Command() => new(Guid.NewGuid(), 100m, "TRY");

    [Fact]
    public async Task Handle_ProviderSucceeds_PersistsSucceededPaymentAndReturnsId()
    {
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-TX-1", null));
        Payment? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Payment>())).Callback<Payment>(payment => added = payment);

        var result = await _sut.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Status.Should().Be(PaymentStatus.Succeeded);
        added.ExternalTransactionId.Should().Be("MOCK-TX-1");
        result.Value.Should().Be(added.Id);
    }

    [Fact]
    public async Task Handle_ProviderFails_PersistsFailedPayment()
    {
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(false, null, "declined"));
        Payment? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Payment>())).Callback<Payment>(payment => added = payment);

        var result = await _sut.Handle(Command(), CancellationToken.None);

        // Command başarılı döner (işlem yapıldı); ödeme sonucu Failed olarak kalıcılaşır.
        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Status.Should().Be(PaymentStatus.Failed);
        added.ExternalTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Always_AddsPaymentToRepositoryOnce()
    {
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-TX-1", null));

        await _sut.Handle(Command(), CancellationToken.None);

        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Once);
    }
}
