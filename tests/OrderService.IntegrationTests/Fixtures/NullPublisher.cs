using MediatR;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Test double: <see cref="IPublisher"/>'ın kasıtlı no-op implementasyonu. Domain event publish
/// davranışını umursamayan persistence/repository testlerinde <see cref="SqlServerContainerFixture"/>
/// tarafından kullanılır; exception swallowing değildir — dispatch gerçekleşmez (K5 ihlali yok).
/// Dispatcher'ı doğrulayan testler Moq <see cref="IPublisher"/> inject eder.
/// </summary>
internal sealed class NullPublisher : IPublisher
{
    public static readonly NullPublisher Instance = new();

    private NullPublisher()
    {
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;
}
