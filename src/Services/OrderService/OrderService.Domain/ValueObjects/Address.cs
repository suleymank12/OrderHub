namespace OrderHub.OrderService.Domain.ValueObjects;

/// <summary>
/// Teslimat adresi value object'i. Tüm alanlar zorunludur (boş/whitespace reddedilir);
/// girdiler trim'lenir. Immutable (record → value equality).
/// </summary>
public sealed record Address
{
    /// <summary>Sokak/cadde satırı.</summary>
    public string Street { get; }

    /// <summary>Şehir.</summary>
    public string City { get; }

    /// <summary>Posta kodu.</summary>
    public string PostalCode { get; }

    /// <summary>Ülke.</summary>
    public string Country { get; }

    private Address(string street, string city, string postalCode, string country)
    {
        Street = street;
        City = city;
        PostalCode = postalCode;
        Country = country;
    }

    /// <summary>Adres üretir. Herhangi bir alan boş/whitespace ise → <see cref="ArgumentException"/>.</summary>
    public static Address Create(string street, string city, string postalCode, string country) =>
        new(
            Require(street, nameof(street)),
            Require(city, nameof(city)),
            Require(postalCode, nameof(postalCode)),
            Require(country, nameof(country)));

    private static string Require(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Address field cannot be empty.", paramName);
        }

        return value.Trim();
    }
}
