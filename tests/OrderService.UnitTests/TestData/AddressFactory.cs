using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>Test verisi üretici (Object Mother): geçerli <see cref="Address"/> örneği.</summary>
internal static class AddressFactory
{
    public static Address Default() => Address.Create("123 Main St", "Istanbul", "34000", "Türkiye");
}
