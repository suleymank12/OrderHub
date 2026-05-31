using Moq;
using OrderHub.Common.Results;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Behaviors;

namespace OrderHub.OrderService.UnitTests.Behaviors;

public sealed class TransactionBehaviorTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    [Fact]
    public async Task Handle_CommandWithSuccess_SavesChangesOnce()
    {
        var sut = new TransactionBehavior<SampleCommand, Result<Guid>>(_unitOfWork.Object);
        var response = Result.Success(Guid.NewGuid());

        await sut.Handle(new SampleCommand(), () => Task.FromResult(response), CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CommandWithSuccess_ReturnsResponseFromNext()
    {
        var sut = new TransactionBehavior<SampleCommand, Result<Guid>>(_unitOfWork.Object);
        var response = Result.Success(Guid.NewGuid());

        var result = await sut.Handle(new SampleCommand(), () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
    }

    [Fact]
    public async Task Handle_CommandWithFailure_DoesNotSaveChanges()
    {
        var sut = new TransactionBehavior<SampleCommand, Result<Guid>>(_unitOfWork.Object);
        var failure = Result.Failure<Guid>(Error.Conflict("Order.Conflict", "boom"));

        await sut.Handle(new SampleCommand(), () => Task.FromResult(failure), CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Query_DoesNotSaveChanges()
    {
        var sut = new TransactionBehavior<SampleQuery, Result<Guid>>(_unitOfWork.Object);
        var response = Result.Success(Guid.NewGuid());

        await sut.Handle(new SampleQuery(), () => Task.FromResult(response), CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Query_ReturnsResponseFromNext()
    {
        var sut = new TransactionBehavior<SampleQuery, Result<Guid>>(_unitOfWork.Object);
        var response = Result.Success(Guid.NewGuid());

        var result = await sut.Handle(new SampleQuery(), () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
    }

    // IBaseCommand → commit edilmesi gereken bir write işlemi.
    internal sealed record SampleCommand : IBaseCommand;

    // IBaseCommand DEĞİL → query; commit edilmemeli.
    internal sealed record SampleQuery;
}
