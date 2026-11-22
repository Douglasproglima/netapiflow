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

    private readonly object _publishLock = new();
    private readonly FlowRecorder _recorder;
    private readonly PersistenceWriter _persistenceWriter;
    private readonly ListenerQueue[] _queues;
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
        _queues = [.. listeners.Select(listener => new ListenerQueue(listener))];
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
        if (_queues.Length == 0)
        {
            return;
        }

        var timestamp = DateTime.UtcNow;
        lock (_publishLock)
        {
            var flowEvent = new FlowEvent
            {
                EventId = Interlocked.Increment(ref _nextEventId),
                Type = type,
                TraceId = traceId,
                Timestamp = timestamp,
                Span = span,
                Trace = trace,
            };

            foreach (var queue in _queues)
            {
                if (!queue.TryEnqueue(flowEvent))
                {
                    FlowMetrics.DroppedEvents.Add(1);
                }
            }
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_queues.Select(queue => Task.Run(() => DeliverAllAsync(queue, stoppingToken), CancellationToken.None)));

    private async Task DeliverAllAsync(ListenerQueue queue, CancellationToken stoppingToken)
    {
        await foreach (var flowEvent in queue.Events.ReadAllAsync(stoppingToken))
        {
            await DeliverAsync(queue.Listener, flowEvent, stoppingToken);
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

    private sealed class ListenerQueue(IFlowEventListener listener)
    {
        private readonly Channel<FlowEvent> _events = Channel.CreateBounded<FlowEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        public IFlowEventListener Listener => listener;

        public ChannelReader<FlowEvent> Events => _events.Reader;

        public bool TryEnqueue(FlowEvent flowEvent) => _events.Writer.TryWrite(flowEvent);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "NetApiFlow: listener {Listener} threw while handling {EventType} for trace {TraceId}")]
        public static partial void ListenerThrew(ILogger logger, string listener, FlowEventType eventType, string traceId, Exception exception);
    }
}
