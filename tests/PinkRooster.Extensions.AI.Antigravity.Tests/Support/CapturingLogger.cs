using Microsoft.Extensions.Logging;

namespace PinkRooster.Extensions.AI.Antigravity.Tests.Support;

/// <summary>An <see cref="ILogger"/> that keeps every entry, with all levels enabled.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    public string Everything()
    {
        lock (Entries)
        {
            return string.Join("\n", Entries.Select(e => $"{e.Level} {e.Id}: {e.Message} {e.Exception}"));
        }
    }
}

internal sealed record LogEntry(LogLevel Level, EventId Id, string Message, Exception? Exception);
