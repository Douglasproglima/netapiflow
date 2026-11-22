using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Storage;

namespace NetApiFlow.Events;

internal sealed partial class FlowEventPublisher : BackgroundService
{
    internal const int QueueCapacity = 5000;

    private static long _nextEventId;

    private readonly Channel<PendingEvent> _queue = Channel.CreateBounded<PendingEvent>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly FlowRecorder _recorder;
    private readonly PersistenceWriter _persistenceWriter;
    private readonly IReadOnlyList<IFlowEventListener> _listeners;
    private readonly ILogger<FlowEventPublisher> _logger;

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

        if (!_queue.Writer.TryWrite(new PendingEvent(type, traceId, DateTime.UtcNow, span, trace)))
        {
            FlowMetrics.DroppedEvents.Add(1);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var flowEvent = pending.ToFlowEvent(Interlocked.Increment(ref _nextEventId));
            foreach (var listener in _listeners)
            {
                await DeliverAsync(listener, flowEvent, stoppingToken);
            }
        }
    }

    private async Task DeliverAsync(IFlowEventListener listener, FlowEvent flowEvent, CancellationToken stoppingToken)
    {
        try
        {
            await listener.OnEventAsync(flowEvent, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            Log.ListenerThrew(_logger, listener.GetType().Name, flowEvent.Type, flowEvent.TraceId, ex);
        }
    }

    public override void Dispose()
    {
        _recorder.TraceStarted -= _onTraceStarted;
        _recorder.TraceCompleted -= _onTraceCompleted;
        _recorder.SpanStarted -= _onSpanStarted;
        _recorder.SpanCompleted -= _onSpanCompleted;
        _recorder.SpanFailed -= _onSpanFailed;
        _persistenceWriter.TraceSaved -= _onTraceSaved;
        base.Dispose();
    }

    private readonly record struct PendingEvent(FlowEventType Type, string TraceId, DateTime Timestamp, FlowSpan? Span, FlowTrace? Trace)
    {
        public FlowEvent ToFlowEvent(long eventId) => new()
        {
            EventId = eventId,
            Type = Type,
            TraceId = TraceId,
            Timestamp = Timestamp,
            Span = Span,
            Trace = Trace,
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "NetApiFlow: listener {Listener} threw while handling {EventType} for trace {TraceId}")]
        public static partial void ListenerThrew(ILogger logger, string listener, FlowEventType eventType, string traceId, Exception exception);
    }
}
