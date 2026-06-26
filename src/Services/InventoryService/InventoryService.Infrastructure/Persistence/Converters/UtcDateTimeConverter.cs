using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderHub.InventoryService.Infrastructure.Persistence.Converters;

/// <summary>
/// SQL Server <c>datetime2</c> <see cref="DateTimeKind"/> tutmaz; geri okunan değer
/// <see cref="DateTimeKind.Unspecified"/> olur. Bu converter okurken Kind'ı <see cref="DateTimeKind.Utc"/>
/// olarak işaretler → tüm domain zamanları (DateTime.UtcNow ile üretilir) round-trip'te UTC kalır
/// (sessiz timezone bug'ı engellenir). OrderService/PaymentService precedent'iyle aynı.
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
