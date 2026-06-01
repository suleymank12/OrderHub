using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OrderHub.OrderService.Api.ExceptionHandling;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.Orders.Exceptions;

namespace OrderHub.OrderService.IntegrationTests.Api;

/// <summary>
/// <c>GlobalExceptionHandler</c>'ın exception → HTTP status / ProblemDetails map'lemesinin doğrudan
/// (HTTP'siz, container'sız) unit testleri. 409 domain exception'ları bu fazda HTTP yolundan erişilemez
/// (Confirm/Cancel endpoint yok) → mapping burada deterministik doğrulanır. Suni throwing endpoint eklenmez.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Handle_ValidationException_MapsTo400WithErrors()
    {
        var exception = new ValidationException([new ValidationFailure("Items", "required")]);

        var problem = await HandleAsync(exception, Environments.Development);

        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Extensions.Should().ContainKey("errors");
    }

    [Fact]
    public async Task Handle_OrderAlreadyConfirmedException_MapsTo409()
    {
        var problem = await HandleAsync(new OrderAlreadyConfirmedException(Guid.NewGuid()), Environments.Development);

        problem.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Handle_InvalidOrderStatusTransitionException_MapsTo409()
    {
        var exception = new InvalidOrderStatusTransitionException(OrderStatus.Cancelled, OrderStatus.Confirmed);

        var problem = await HandleAsync(exception, Environments.Development);

        problem.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Handle_EmptyOrderException_MapsTo400_NotConflict()
    {
        // EmptyOrder = geçersiz girdi → 400 (state conflict 409 DEĞİL).
        var problem = await HandleAsync(new EmptyOrderException(), Environments.Development);

        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Handle_UnexpectedException_InProduction_Hides500Detail()
    {
        var problem = await HandleAsync(new InvalidOperationException("sensitive internal detail"), Environments.Production);

        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().Be("An unexpected error occurred.");
        problem.Detail.Should().NotContain("sensitive");
    }

    [Fact]
    public async Task Handle_UnexpectedException_InDevelopment_ExposesDetail()
    {
        var problem = await HandleAsync(new InvalidOperationException("dev visible detail"), Environments.Development);

        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().Be("dev visible detail");
    }

    private static async Task<ProblemDetails> HandleAsync(Exception exception, string environmentName)
    {
        var problemDetailsService = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            new FakeHostEnvironment(environmentName),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(), exception, CancellationToken.None);

        handled.Should().BeTrue();
        return problemDetailsService.Captured!.ProblemDetails;
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Captured { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Captured = context;
            return new ValueTask<bool>(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Captured = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
