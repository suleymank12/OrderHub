using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.Application.Behaviors;

namespace OrderHub.OrderService.Application;

/// <summary>
/// Application katmanının DI kayıtları. Yalnızca bu katmanın servislerini bağlar; repository/UnitOfWork
/// gibi port implementasyonları Infrastructure'ın <c>AddInfrastructure</c>'ında (Faz 1.4) kayıtlanır.
/// </summary>
public static class DependencyInjection
{
    /// <summary>MediatR + pipeline behavior'lar, FluentValidation validator'ları ve Mapster'ı kayıtlar.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Sıra önemli: önce kayıtlanan en dışta çalışır.
            // Logging (her şeyi ölçer) → Validation (geçersiz input'ta DB açılmaz) → Transaction (commit).
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Mapster: assembly'deki IRegister konfigürasyonlarını (OrderMappingConfig) tara, IMapper'ı bağla.
        var typeAdapterConfig = TypeAdapterConfig.GlobalSettings;
        typeAdapterConfig.Scan(assembly);
        services.AddSingleton(typeAdapterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
