using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>Yakalanan tek bir log kaydı (yalnız EventId + exception tipi — payload tutulmaz).</summary>
internal sealed record CapturedLog(int EventId, string? ExceptionType);

/// <summary>
/// 3d-4b ampirik gözlem için minimal log yakalayıcı. Amaç: processor broker-down'da publish denerken
/// <c>PublishDeferred</c> (EventId 3004) log'unun <b>exception tipini</b> okumak → MassTransit publish'i
/// FIRLATTI mı (transport connection exception) yoksa BLOKE mi etti (PublishTimeout iptali → TaskCanceled).
/// Test double; exception swallowing değildir (yalnız gözlemler).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _logs = new();

    public IReadOnlyCollection<CapturedLog> Logs => _logs.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => logs.Enqueue(new CapturedLog(eventId.Id, exception?.GetType().Name));
    }
}
