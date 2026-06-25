using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.PaymentService.Application.Abstractions.Payments;
using OrderHub.PaymentService.Application.Behaviors;
using OrderHub.PaymentService.Application.Payments;

namespace OrderHub.PaymentService.Application;

/// <summary>
/// Application katmanının DI kayıtları: MediatR + pipeline behavior'lar, FluentValidation, Mapster ve mock
/// ödeme sağlayıcısı. Port implementasyonları (repository/UnitOfWork) Infrastructure'da kayıtlanır.
/// </summary>
public static class DependencyInjection
{
    /// <summary>MediatR + behavior'lar, validator'lar, Mapster ve <see cref="IPaymentProcessor"/>'ı kayıtlar.</summary>
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

        var typeAdapterConfig = TypeAdapterConfig.GlobalSettings;
        typeAdapterConfig.Scan(assembly);
        services.AddSingleton(typeAdapterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        // Mock sağlayıcı stateless (Random.Shared + IOptions) → singleton. Gerçek adapter ileride aynı port'la.
        services.AddSingleton<IPaymentProcessor, MockPaymentProcessor>();

        return services;
    }
}
