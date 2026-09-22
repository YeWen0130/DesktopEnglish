using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Tests;

/// <summary>Silently discards log output; keeps tests free of any logging setup.</summary>
internal sealed class NoOpLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
