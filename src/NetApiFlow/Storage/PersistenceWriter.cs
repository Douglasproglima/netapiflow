using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;

namespace NetApiFlow.Storage;

/// <summary>
/// Consumes completed traces from a bounded channel and saves them via <see cref="ITraceStore"/>. The
/// request thread never performs persistence I/O directly (seção 6.6 do prompt).
/// </summary>
internal sealed partial class PersistenceWriter : BackgroundService
{
    private const int Capacity = 1000;

    private readonly Channel<(FlowTrace Trace, IReadOnlyList<FlowSpan> Spans)> _channel =
        Channel.CreateBounded<(FlowTrace, IReadOnlyList<FlowSpan>)>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly FlowRecorder _recorder;
    private readonly ITraceStore _store;
    private readonly IOptions<NetApiFlowOptions> _options;
    private readonly ILogger<PersistenceWriter> _logger;
    private readonly Action<FlowTrace, IReadOnlyList<FlowSpan>> _onTraceReady;
    private long _droppedCount;
    private long _failureCount;

    public PersistenceWriter(FlowRecorder recorder, ITraceStore store, IOptions<NetApiFlowOptions> options, ILogger<PersistenceWriter> logger)
    {
        _recorder = recorder;
        _store = store;
        _options = options;
        _logger = logger;
        _onTraceReady = Enqueue;
        _recorder.TraceReadyForPersistence += _onTraceReady;
    }

    /// <summary>Raised after a trace is saved to the configured <see cref="ITraceStore"/>.</summary>
    public event Action<FlowTrace>? TraceSaved;

    /// <summary>Number of traces discarded because the persistence queue was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>Number of traces that failed to persist.</summary>
    public long FailureCount => Interlocked.Read(ref _failureCount);

    private void Enqueue(FlowTrace trace, IReadOnlyList<FlowSpan> spans)
    {
        var options = _options.Value;
        if (!TailSampleDecision.ShouldPersist(trace, options.TailSampling, options.SlowThreshold, Random.Shared.NextDouble()))
        {
            FlowMetrics.SampledOutTraces.Add(1);
            return;
        }

        if (!_channel.Writer.TryWrite((trace, spans)))
        {
            Interlocked.Increment(ref _droppedCount);
            FlowMetrics.PersistenceFailures.Add(1);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (trace, spans) in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _store.SaveAsync(trace, spans, stoppingToken);
                TraceSaved?.Invoke(trace);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _failureCount);
                FlowMetrics.PersistenceFailures.Add(1);
                Log.PersistenceFailed(_logger, trace.TraceId, ex);
            }
        }
    }

    public override void Dispose()
    {
        _recorder.TraceReadyForPersistence -= _onTraceReady;
        base.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "NetApiFlow: failed to persist trace {TraceId}")]
        public static partial void PersistenceFailed(ILogger logger, string traceId, Exception exception);
    }
}
