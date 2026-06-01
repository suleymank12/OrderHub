using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderHub.OrderService.Infrastructure.Persistence.Converters;

/// <summary>
/// SQL Server <c>datetime2</c> <see cref="DateTimeKind"/> tutmaz; geri okunan değer
/// <see cref="DateTimeKind.Unspecified"/> olur. Bu converter okurken Kind'ı <see cref="DateTimeKind.Utc"/>
/// olarak işaretler → tüm domain zamanları (DateTime.UtcNow ile üretilir) round-trip'te UTC kalır.
/// Aksi halde "Unspecified" değerler sessiz timezone bug'larına yol açar (yerel saate yorumlanma).
/// </summary>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

/// <summary>Nullable <see cref="DateTime"/> kolonları için UTC kind koruyan converter.</summary>
internal sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value)
    {
    }
}
