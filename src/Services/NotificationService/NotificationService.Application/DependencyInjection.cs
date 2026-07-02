using Microsoft.Extensions.DependencyInjection;

namespace OrderHub.NotificationService.Application;

/// <summary>
/// Application katmanının DI kayıtları. 5f-1 iskeleti: şimdilik servis yok → 5f-2'de
/// <c>CartAbandonmentReminderJob</c> + <c>IEmailSender</c> buraya eklenir.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
