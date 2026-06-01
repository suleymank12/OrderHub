using OrderHub.OrderService.Application.Orders.Commands.CreateOrder;
using OrderHub.OrderService.Application.ValueObjects.Dtos;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>
/// Test verisi üretici (Object Mother): geçerli <see cref="CreateOrderCommand"/> ve inbound DTO örnekleri.
/// Domain factory'lerinden geçeceği için tüm değerler default olarak geçerlidir; test'ler tekil alanları
/// override ederek edge-case kurar.
/// </summary>
internal static class CreateOrderRequestFactory
{
    public static AddressDto Address() => new("123 Main St", "Istanbul", "34000", "Türkiye");

    public static MoneyDto Money(decimal amount = 100m, string currency = "TRY") => new(amount, currency);

    public static CreateOrderItemRequest Item(decimal amount = 100m, string currency = "TRY", int quantity = 1) =>
        new(Guid.NewGuid(), quantity, Money(amount, currency));

    public static CreateOrderCommand ValidCommand(IReadOnlyList<CreateOrderItemRequest>? items = null) =>
        new(Address(), items ?? [Item()]);
}
