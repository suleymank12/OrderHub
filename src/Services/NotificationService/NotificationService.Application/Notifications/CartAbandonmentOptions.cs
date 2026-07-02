namespace OrderHub.NotificationService.Application.Notifications;

/// <summary>
/// Sepet-terk hatırlatma politikası. <see cref="ReminderDelay"/> sonunda Created/Confirmed durumundaki
/// (henüz ödenmemiş) sipariş için e-posta gönderilir (ROADMAP 5f-2: 1 saat). Config bölümü:
/// <c>CartAbandonment</c> → <see cref="SectionName"/>.
/// </summary>
public sealed class CartAbandonmentOptions
{
    /// <summary>appsettings bölüm adı.</summary>
    public const string SectionName = "CartAbandonment";

    /// <summary>
    /// Sipariş oluşturulduktan bu süre sonra hatırlatma Hangfire'a zamanlanır (varsayılan 1 saat).
    /// </summary>
    public TimeSpan ReminderDelay { get; init; } = TimeSpan.FromHours(1);
}
