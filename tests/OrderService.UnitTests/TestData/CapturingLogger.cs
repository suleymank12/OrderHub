using Microsoft.Extensions.Logging;

namespace OrderHub.OrderService.UnitTests.TestData;

/// <summary>
/// Yayılan log kayıtlarını yakalayan test <see cref="ILogger{TCategoryName}"/>'ı. <c>NullLogger</c> ile
/// log <b>içeriği</b> assert edilemez; bu logger render edilmiş mesajları toplar → LoggingBehavior'ın
/// PII testi (mesajlarda CustomerId/Address bulunmamalı) bununla yapılır.
/// </summary>
/// <typeparam name="T">Log kategorisi tipi.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>Yakalanan kayıtlar (seviye + render edilmiş mesaj).</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
    }
}

/// <summary>Yakalanmış tek bir log kaydı.</summary>
/// <param name="Level">Log seviyesi.</param>
/// <param name="EventId">Log şablonunun kayıt kimliği (source-gen <c>[LoggerMessage]</c> EventId).</param>
/// <param name="Message">Render edilmiş mesaj metni.</param>
internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message);
