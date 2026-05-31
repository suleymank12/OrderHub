using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.Infrastructure;

namespace OrderHub.OrderService.IntegrationTests;

/// <summary>
/// <see cref="DependencyInjection.AddInfrastructure"/> kayıt davranışı. Container gerektirmez (DI/config
/// seviyesi) → koleksiyon dışında. Connection string eksikse fail-fast doğrulanır: cryptic EF runtime
/// hatası yerine net <see cref="InvalidOperationException"/> (K3/K5 — yanlış konfigürasyon erken yakalanır).
/// </summary>
public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build(); // boş → connection string yok.

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>();
    }
}
