using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderHub.NotificationService.Infrastructure.Persistence.Converters;

/// <summary>
/// SQL Server <c>datetime2</c> <see cref="DateTimeKind"/> tutmaz; geri okunan değer
/// <see cref="DateTimeKind.Unspecified"/> olur. Bu converter okurken Kind'ı <see cref="DateTimeKind.Utc"/>
/// olarak işaretler → tüm zamanlar (UTC üretilir) round-trip'te UTC kalır. AnalyticsService precedent'iyle aynı.
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
