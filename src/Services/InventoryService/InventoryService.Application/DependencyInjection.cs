using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.InventoryService.Application.Behaviors;

namespace OrderHub.InventoryService.Application;

/// <summary>
/// Application katmanının DI kayıtları: MediatR + pipeline behavior'lar ve FluentValidation.
/// Port implementasyonları (repository/UnitOfWork) Infrastructure'da kayıtlanır.
/// NOT: Mapster/MapsterMapper eklenmemiştir — InventoryService salt komut güdümlüdür; okuma DTO'su veya
/// query handler'ı bulunmaz. Kullanılmayan bağımlılık eklemek K2 ihlali sayılır.
/// </summary>
public static class DependencyInjection
{
    /// <summary>MediatR + behavior'lar ve validator'ları kayıtlar.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Sıra: Logging (her şeyi ölçer) → Validation (geçersiz input'ta DB açılmaz) → Transaction (commit).
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
