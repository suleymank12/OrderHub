using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Outbox.Interceptors;

namespace OrderHub.Outbox;

/// <summary>
/// <see cref="DbContextOptionsBuilder"/> için outbox seam'i. Pre-commit interceptor'ı servisin DbContext
/// options'ına ekler; <c>OutboxInterceptor</c> <b>internal</b> kalır (servis onu new'lemez/göremez) ve
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxWriter"/> ile kaydedilen singleton DI'dan resolve edilir.
/// </summary>
public static class OutboxDbContextOptionsBuilderExtensions
{
    /// <summary>Pre-commit <c>OutboxInterceptor</c>'ı DbContext options'ına ekler.</summary>
    public static DbContextOptionsBuilder AddOutboxInterceptor(
        this DbContextOptionsBuilder options, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return options.AddInterceptors(serviceProvider.GetRequiredService<OutboxInterceptor>());
    }
}
