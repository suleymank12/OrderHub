using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Identity;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.Commands.CreateOrder;

public sealed class CreateOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly CreateOrderCommandHandler _sut;

    public CreateOrderCommandHandlerTests()
    {
        _sut = new CreateOrderCommandHandler(
            _repository.Object,
            _currentUser.Object,
            NullLogger<CreateOrderCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_AuthenticatedUser_ReturnsSuccessWithCreatedOrderId()
    {
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        Order? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(order => added = order);

        var result = await _sut.Handle(CreateOrderRequestFactory.ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(added!.Id);
    }

    [Fact]
    public async Task Handle_AuthenticatedUser_UsesCurrentUserIdNotRequestBody()
    {
        // K3 (IDOR): müşteri kimliği auth context'ten gelmeli, request body'sinden değil.
        var authenticatedUserId = Guid.NewGuid();
        _currentUser.Setup(u => u.UserId).Returns(authenticatedUserId);
        Order? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(order => added = order);

        await _sut.Handle(CreateOrderRequestFactory.ValidCommand(), CancellationToken.None);

        added!.CustomerId.Should().Be(authenticatedUserId);
    }

    [Fact]
    public async Task Handle_ValidCommand_AddsOrderToRepositoryOnce()
    {
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        await _sut.Handle(CreateOrderRequestFactory.ValidCommand(), CancellationToken.None);

        _repository.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_BuildsOrderWithAddressItemsAndTotalViaDomainFactories()
    {
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        Order? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(order => added = order);
        var command = CreateOrderRequestFactory.ValidCommand(
            [CreateOrderRequestFactory.Item(amount: 50m, quantity: 2), CreateOrderRequestFactory.Item(amount: 30m)]);

        await _sut.Handle(command, CancellationToken.None);

        added!.Items.Should().HaveCount(2);
        added.ShippingAddress.City.Should().Be("Istanbul");
        added.Total.Amount.Should().Be(130m);
        added.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsAndDoesNotPersist()
    {
        // Seçenek A: handler authentication'ı assume eder; auth yoksa UserId=Guid.Empty →
        // Order.Create reddeder (kaza-koruma). Asıl enforcement [Authorize] (Faz 1.5).
        _currentUser.Setup(u => u.UserId).Returns(Guid.Empty);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);

        var act = () => _sut.Handle(CreateOrderRequestFactory.ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _repository.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnsupportedCurrency_ThrowsAndDoesNotPersist()
    {
        // Handler validated input'a güvenir; geçersiz currency upstream'de yakalanır (validator).
        // Bypass edilirse handler sessizce kötü veri yazmaz, fail-loud olur.
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        var command = CreateOrderRequestFactory.ValidCommand([CreateOrderRequestFactory.Item(currency: "XXX")]);

        var act = () => _sut.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _repository.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }
}
