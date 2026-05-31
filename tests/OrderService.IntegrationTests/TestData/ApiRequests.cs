using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.IntegrationTests.TestData;

/// <summary>Api integration test'leri için geçerli inbound request üreticisi (Object Mother).</summary>
internal static class ApiRequests
{
    public static AddressDto Address() => new("123 Main St", "Istanbul", "34000", "Türkiye");

    public static CreateOrderRequest ValidCreateOrder() =>
        new(Address(), [new CreateOrderItemRequest(Guid.NewGuid(), 2, new MoneyDto(50m, "TRY"))]);

    public static CreateOrderRequest WithItems(IReadOnlyList<CreateOrderItemRequest> items) =>
        new(Address(), items);
}
