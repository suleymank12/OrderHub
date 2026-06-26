using OrderHub.Common.Primitives;

namespace OrderHub.InventoryService.Domain.Catalog;

/// <summary>
/// Katalog ürünü aggregate root'u — <b>hafif</b> (kimlik + ad + SKU). <b>Stok TAŞIMAZ:</b> stok seviyesi ve
/// rezervasyonlar ayrı <c>StockItem</c> aggregate'indedir (farklı yaşam döngüsü + contention sınırı). StockItem
/// buraya yalnızca <c>ProductId</c> ile referans verir (aggregate'ler arası id-ref, ortak tutarlılık sınırı yok).
/// Hiçbir setter public değil.
/// </summary>
public sealed class Product : AggregateRoot<Guid>
{
    private Product()
    {
    }

    /// <summary>Ürün adı.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Stok tutma birimi kodu (SKU); opsiyonel.</summary>
    public string? Sku { get; private set; }

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Yeni katalog ürünü oluşturur. <paramref name="name"/> boş → <see cref="ArgumentException"/>.
    /// <paramref name="sku"/> opsiyonel (boş/whitespace → null'a normalize edilir).
    /// </summary>
    public static Product Create(string name, string? sku = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
