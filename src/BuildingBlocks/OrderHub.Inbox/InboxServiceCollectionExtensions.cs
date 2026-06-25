using Microsoft.Extensions.DependencyInjection;
using OrderHub.Inbox.Consuming;

namespace OrderHub.Inbox;

/// <summary>
/// Inbox altyapısını kaydeden DI uzantısı. Open-generic message-level <see cref="InboxConsumeFilter{TMessage}"/>'ı
/// <b>scoped</b> kaydeder → MassTransit her mesajda consume-scope'tan resolve eder (scoped
/// <see cref="IInboxDbContext"/> ile aynı scope, atomiklik için şart). Filter'ı consume pipeline'ına bağlama
/// (<c>UseConsumeFilter(typeof(InboxConsumeFilter&lt;&gt;), context)</c>) ve <see cref="IInboxDbContext"/>'in
/// servis DbContext'ine bağlanması composition root'un işidir.
/// </summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>Open-generic inbox consume filter'ını scoped kaydeder.</summary>
    public static IServiceCollection AddInbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped(typeof(InboxConsumeFilter<>));

        return services;
    }
}
