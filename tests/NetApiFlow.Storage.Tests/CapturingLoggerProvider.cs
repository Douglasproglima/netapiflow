using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NetApiFlow.Storage.Tests;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();

    public IReadOnlyList<LogLine> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    internal readonly record struct LogLine(LogLevel Level, string Message);

    private sealed class CapturingLogger(ConcurrentQueue<LogLine> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(new LogLine(logLevel, formatter(state, exception)));
    }
}
