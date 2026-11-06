using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Recording;
using NetApiFlow.Sanitization;

namespace NetApiFlow.Instrumentation;

/// <summary>Correlates a log call to the span active in <see cref="Activity.Current"/>, when it
/// belongs to the <see cref="FlowActivitySource"/> (ADR-0017).</summary>
internal sealed class FlowLogger(FlowRecorder recorder, IOptions<NetApiFlowOptions> options) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= options.Value.LogCorrelation.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (Activity.Current is not { } activity || activity.Source.Name != FlowActivitySource.Name)
        {
            return;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Message = ErrorMessageSanitizer.Sanitize(formatter(state, exception)) ?? string.Empty,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = ErrorMessageSanitizer.Sanitize(exception?.Message),
        };

        recorder.AddLogEntry(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), entry);
    }
}
