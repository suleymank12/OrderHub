using Microsoft.Extensions.DependencyInjection;
using OrderHub.Inbox.Consuming;

namespace OrderHub.Inbox;

/// <summary>
/// Inbox altyapısını kaydeden DI uzantısı. Open-generic <see cref="InboxConsumeFilter{TConsumer,TMessage}"/>'ı
/// <b>scoped</b> kaydeder → MassTransit her mesajda consume-scope'tan resolve eder (scoped
/// <see cref="IInboxDbContext"/> ile aynı scope, atomiklik için şart). Filter'ı consumer pipeline'ına bağlama
/// (<c>UseConsumeFilter</c>) ve <see cref="IInboxDbContext"/>'in servis DbContext'ine bağlanması composition
/// root'un işidir (3d-2; bu adımda çağrılmaz → davranış değişmez).
/// </summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>Open-generic inbox consume filter'ını scoped kaydeder.</summary>
    public static IServiceCollection AddInbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped(typeof(InboxConsumeFilter<,>));

        return services;
    }
}
