using OrderHub.InventoryService.Domain.Catalog;

namespace OrderHub.InventoryService.UnitTests.Catalog;

/// <summary>
/// <see cref="Product"/> katalog aggregate testleri (5b). Hafif: ad zorunlu, SKU opsiyonel/normalize.
/// </summary>
public sealed class ProductTests
{
    [Fact]
    public void Create_ValidName_SetsTrimmedNameAndSku()
    {
        var product = Product.Create("  Widget  ", "  SKU-1  ");

        product.Name.Should().Be("Widget");
        product.Sku.Should().Be("SKU-1");
        product.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_BlankSku_NormalizesToNull()
    {
        Product.Create("Widget", "   ").Sku.Should().BeNull();
        Product.Create("Widget").Sku.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string name)
    {
        var act = () => Product.Create(name);

        act.Should().Throw<ArgumentException>();
    }
}
