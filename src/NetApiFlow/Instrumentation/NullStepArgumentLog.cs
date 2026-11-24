using Microsoft.Extensions.Logging;
using NetApiFlow.Storage;

namespace NetApiFlow.Instrumentation;

internal sealed partial class NullStepArgumentLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static NullStepArgumentLog? _current;

    private readonly ILogger _logger;
    private readonly IntervalLogLimiter _step = new(Interval);
    private readonly IntervalLogLimiter _tag = new(Interval);
    private readonly IntervalLogLimiter _error = new(Interval);

    private NullStepArgumentLog(ILogger logger)
    {
        _logger = logger;
    }

    public static void Attach(ILogger logger) => _current = new NullStepArgumentLog(logger);

    public static void Detach(ILogger logger)
    {
        var current = _current;
        if (current is not null && ReferenceEquals(current._logger, logger))
        {
            Interlocked.CompareExchange(ref _current, null, current);
        }
    }

    public static void StepIgnored()
    {
        if (_current is { } current && current._step.TryAcquire())
        {
            Log.NullStepArgument(current._logger);
        }
    }

    public static void TagIgnored()
    {
        if (_current is { } current && current._tag.TryAcquire())
        {
            Log.NullTagArgument(current._logger);
        }
    }

    public static void ErrorIgnored()
    {
        if (_current is { } current && current._error.TryAcquire())
        {
            Log.NullErrorArgument(current._logger);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: Flow.Step was called with a null component or operation, so that step is not recorded. The request was not affected. This is logged at most once every 30 seconds.")]
        public static partial void NullStepArgument(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: FlowStep.Tag was called with a null key or value, so that tag is not recorded. The request was not affected. This is logged at most once every 30 seconds.")]
        public static partial void NullTagArgument(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: FlowStep.MarkError was called with a null exception, so no error is recorded on that step. The request was not affected. This is logged at most once every 30 seconds.")]
        public static partial void NullErrorArgument(ILogger logger);
    }
}
