using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.AnalyticsService.Application.Behaviors;

namespace OrderHub.AnalyticsService.Application;

/// <summary>
/// Application katmanının DI kayıtları: MediatR + pipeline behavior'lar, FluentValidation ve Mapster.
/// <b>TransactionBehavior YOK</b>: AnalyticsService read-only'dir (write-command yok) → açacak transaction da yok.
/// Read-side repository implementasyonu (port: <c>IAnalyticsReadRepository</c>) Infrastructure'da kayıtlanır (DIP).
/// </summary>
public static class DependencyInjection
{
    /// <summary>MediatR + Logging/Validation behavior'ları, validator'ları ve Mapster'ı kayıtlar.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Sıra: Logging (her şeyi ölçer) → Validation (geçersiz input'ta handler'a inilmez). Transaction yok (read-only).
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        var typeAdapterConfig = TypeAdapterConfig.GlobalSettings;
        typeAdapterConfig.Scan(assembly);
        services.AddSingleton(typeAdapterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
