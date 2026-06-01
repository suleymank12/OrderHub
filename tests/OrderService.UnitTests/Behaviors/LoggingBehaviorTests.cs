using Microsoft.Extensions.Logging;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Behaviors;
using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Behaviors;

public sealed class LoggingBehaviorTests
{
    private readonly CapturingLogger<LoggingBehavior<CreateOrderCommand, Result<Guid>>> _logger = new();
    private readonly LoggingBehavior<CreateOrderCommand, Result<Guid>> _sut;

    public LoggingBehaviorTests() => _sut = new LoggingBehavior<CreateOrderCommand, Result<Guid>>(_logger);

    [Fact]
    public async Task Handle_SuccessfulResult_LogsHandledAtInformation()
    {
        var command = CreateOrderRequestFactory.ValidCommand();
        var expected = Result.Success(Guid.NewGuid());

        var result = await _sut.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        result.Should().BeSameAs(expected);
        _logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("handled in"));
    }

    [Fact]
    public async Task Handle_FailedResult_LogsFailureWithErrorCodeAtWarning()
    {
        var command = CreateOrderRequestFactory.ValidCommand();
        var failure = Result.Failure<Guid>(Error.Validation("Order.Invalid", "boom"));

        await _sut.Handle(command, () => Task.FromResult(failure), CancellationToken.None);

        _logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Order.Invalid"));
    }

    [Fact]
    public async Task Handle_Always_InvokesNextExactlyOnce()
    {
        var command = CreateOrderRequestFactory.ValidCommand();
        var invocations = 0;

        await _sut.Handle(
            command,
            () =>
            {
                invocations++;
                return Task.FromResult(Result.Success(Guid.NewGuid()));
            },
            CancellationToken.None);

        invocations.Should().Be(1);
    }

    [Fact]
    public async Task Handle_RequestWithPii_DoesNotLogRequestContent()
    {
        // Address PII (sokak, şehir, posta kodu, ülke) içeren gerçek bir command ile çalıştır;
        // hiçbir log mesajı bu içeriği taşımamalı (§8, K3 — request gövdesi loglanmaz).
        var command = CreateOrderRequestFactory.ValidCommand();
        var response = Result.Success(Guid.NewGuid());

        await _sut.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        _logger.Entries.Should().NotBeEmpty();
        _logger.Entries.Should().OnlyContain(entry =>
            !entry.Message.Contains("Main St")
            && !entry.Message.Contains("Istanbul")
            && !entry.Message.Contains("Türkiye")
            && !entry.Message.Contains("34000"));
    }
}
