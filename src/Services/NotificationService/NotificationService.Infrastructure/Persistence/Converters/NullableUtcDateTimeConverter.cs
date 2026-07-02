using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderHub.NotificationService.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="UtcDateTimeConverter"/>'ın nullable karşılığı (ör. <c>OrderProjection.ReminderSentUtc</c>):
/// okurken değer varsa Kind'ı <see cref="DateTimeKind.Utc"/> işaretler, null ise null kalır.
/// </summary>
internal sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value)
    {
    }
}
