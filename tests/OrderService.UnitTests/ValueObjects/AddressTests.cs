using OrderHub.OrderService.Domain.ValueObjects;

namespace OrderHub.OrderService.UnitTests.ValueObjects;

public sealed class AddressTests
{
    [Fact]
    public void Create_ValidFields_SetsAndTrimsFields()
    {
        var address = Address.Create("  Main St  ", " Istanbul ", " 34000 ", " Türkiye ");

        address.Street.Should().Be("Main St");
        address.City.Should().Be("Istanbul");
        address.PostalCode.Should().Be("34000");
        address.Country.Should().Be("Türkiye");
    }

    [Theory]
    [InlineData("", "City", "34000", "Country")]
    [InlineData("Street", "", "34000", "Country")]
    [InlineData("Street", "City", "", "Country")]
    [InlineData("Street", "City", "34000", "")]
    [InlineData("Street", "City", "34000", "   ")]
    public void Create_EmptyOrWhitespaceField_ThrowsArgumentException(
        string street, string city, string postalCode, string country)
    {
        var act = () => Address.Create(street, city, postalCode, country);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        Address.Create("Main St", "Istanbul", "34000", "Türkiye")
            .Should().Be(Address.Create("Main St", "Istanbul", "34000", "Türkiye"));
    }
}
