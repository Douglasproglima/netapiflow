using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Storage;

namespace NetApiFlow.Events;

/// <summary>
/// Bridges <see cref="FlowRecorder"/> and <see cref="PersistenceWriter"/> state changes into
/// <see cref="IFlowEventListener"/> instances. Dispatch never blocks the caller and a failing
/// listener never affects the others (seção 6.5 do prompt).
/// </summary>
internal sealed partial class FlowEventPublisher : IDisposable
{
    private readonly FlowRecorder _recorder;
    private readonly PersistenceWriter _persistenceWriter;
    private readonly IReadOnlyList<IFlowEventListener> _listeners;
    private readonly ILogger<FlowEventPublisher> _logger;
    private static long _nextEventId;

    private readonly Action<FlowTrace> _onTraceStarted;
    private readonly Action<FlowTrace> _onTraceCompleted;
    private readonly Action<FlowSpan> _onSpanStarted;
    private readonly Action<FlowSpan> _onSpanCompleted;
    private readonly Action<FlowSpan> _onSpanFailed;
    private readonly Action<FlowTrace> _onTraceSaved;

    public FlowEventPublisher(
        FlowRecorder recorder,
        PersistenceWriter persistenceWriter,
        IEnumerable<IFlowEventListener> listeners,
        ILogger<FlowEventPublisher> logger)
    {
        _recorder = recorder;
        _persistenceWriter = persistenceWriter;
        _listeners = [.. listeners];
        _logger = logger;

        _onTraceStarted = trace => Publish(FlowEventType.TraceStarted, trace.TraceId, trace: trace);
        _onTraceCompleted = trace => Publish(FlowEventType.TraceCompleted, trace.TraceId, trace: trace);
        _onSpanStarted = span => Publish(FlowEventType.SpanStarted, span.TraceId, span: span);
        _onSpanCompleted = span => Publish(FlowEventType.SpanCompleted, span.TraceId, span: span);
        _onSpanFailed = span => Publish(FlowEventType.SpanFailed, span.TraceId, span: span);
        _onTraceSaved = trace => Publish(FlowEventType.TracePersisted, trace.TraceId, trace: trace);

        _recorder.TraceStarted += _onTraceStarted;
        _recorder.TraceCompleted += _onTraceCompleted;
        _recorder.SpanStarted += _onSpanStarted;
        _recorder.SpanCompleted += _onSpanCompleted;
        _recorder.SpanFailed += _onSpanFailed;
        _persistenceWriter.TraceSaved += _onTraceSaved;
    }

    private void Publish(FlowEventType type, string traceId, FlowSpan? span = null, FlowTrace? trace = null)
    {
        if (_listeners.Count == 0)
        {
            return;
        }

        var flowEvent = new FlowEvent
        {
            EventId = Interlocked.Increment(ref _nextEventId),
            Type = type,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow,
            Span = span,
            Trace = trace,
        };

        _ = DispatchAsync(flowEvent);
    }

    private async Task DispatchAsync(FlowEvent flowEvent)
    {
        foreach (var listener in _listeners)
        {
            try
            {
                await listener.OnEventAsync(flowEvent, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.ListenerThrew(_logger, listener.GetType().Name, flowEvent.Type, flowEvent.TraceId, ex);
            }
        }
    }

    public void Dispose()
    {
        _recorder.TraceStarted -= _onTraceStarted;
        _recorder.TraceCompleted -= _onTraceCompleted;
        _recorder.SpanStarted -= _onSpanStarted;
        _recorder.SpanCompleted -= _onSpanCompleted;
        _recorder.SpanFailed -= _onSpanFailed;
        _persistenceWriter.TraceSaved -= _onTraceSaved;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "NetApiFlow: listener {Listener} threw while handling {EventType} for trace {TraceId}")]
        public static partial void ListenerThrew(ILogger logger, string listener, FlowEventType eventType, string traceId, Exception exception);
    }
}
