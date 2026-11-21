using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;

namespace NetApiFlow.Storage;

internal sealed partial class PersistenceWriter : BackgroundService
{
    private const int Capacity = 1000;
    private const int FailuresInARowBeforeTheCircuitOpens = 3;

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
    private readonly TimeSpan _retryInterval;
    private readonly TimeProvider _clock;
    private long _droppedCount;
    private long _failureCount;
    private long _notSavedSinceTheFirstFailure;
    private int _failuresInARow;
    private DateTimeOffset? _retryAfter;

    public PersistenceWriter(
        FlowRecorder recorder,
        ITraceStore store,
        IOptions<NetApiFlowOptions> options,
        ILogger<PersistenceWriter> logger,
        PersistenceRetryInterval? retryInterval = null,
        TimeProvider? timeProvider = null)
    {
        _recorder = recorder;
        _store = store;
        _options = options;
        _logger = logger;
        _retryInterval = (retryInterval ?? PersistenceRetryInterval.Default).Value;
        _clock = timeProvider ?? TimeProvider.System;
        _onTraceReady = Enqueue;
        _recorder.TraceReadyForPersistence += _onTraceReady;
    }

    public event Action<FlowTrace>? TraceSaved;

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

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
            if (IsWaitingToRetry())
            {
                DiscardWhileWaiting();
                continue;
            }

            try
            {
                await _store.SaveAsync(trace, spans, stoppingToken);
                TraceSaved?.Invoke(trace);
                RecordSuccess();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                RecordFailure(trace.TraceId, ex);
            }
        }
    }

    private bool IsWaitingToRetry() => _retryAfter is { } retryAfter && _clock.GetUtcNow() < retryAfter;

    private void DiscardWhileWaiting()
    {
        _notSavedSinceTheFirstFailure++;
        Interlocked.Increment(ref _failureCount);
        FlowMetrics.PersistenceFailures.Add(1);
    }

    private void RecordFailure(string traceId, Exception exception)
    {
        _notSavedSinceTheFirstFailure++;
        _failuresInARow++;
        Interlocked.Increment(ref _failureCount);
        FlowMetrics.PersistenceFailures.Add(1);

        if (_failuresInARow < FailuresInARowBeforeTheCircuitOpens)
        {
            Log.PersistenceFailed(_logger, traceId, exception.GetType().Name, exception.Message, FailuresInARowBeforeTheCircuitOpens);
        }
        else
        {
            _retryAfter = _clock.GetUtcNow() + _retryInterval;
            Log.PersistenceSuspended(_logger, traceId, exception.GetType().Name, exception.Message, _failuresInARow, _retryInterval.TotalSeconds);
        }

        Log.PersistenceFailureDetail(_logger, traceId, exception);
    }

    private void RecordSuccess()
    {
        var circuitWasOpen = _retryAfter is not null;
        var notSaved = _notSavedSinceTheFirstFailure;
        _failuresInARow = 0;
        _notSavedSinceTheFirstFailure = 0;
        _retryAfter = null;

        if (circuitWasOpen)
        {
            Log.PersistenceRecovered(_logger, notSaved);
        }
    }

    public override void Dispose()
    {
        _recorder.TraceReadyForPersistence -= _onTraceReady;
        base.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: the store failed to save trace {TraceId} ({ExceptionType}: {ExceptionMessage}). The next trace is tried normally. After {FailuresInARow} failures in a row, traces are discarded for a while.")]
        public static partial void PersistenceFailed(ILogger logger, string traceId, string exceptionType, string exceptionMessage, int failuresInARow);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: the store failed to save trace {TraceId} ({ExceptionType}: {ExceptionMessage}). {FailuresInARow} saves failed in a row, so traces are discarded for {RetryAfterSeconds} seconds and then tried again. The count is netapiflow.persistence.failures.")]
        public static partial void PersistenceSuspended(ILogger logger, string traceId, string exceptionType, string exceptionMessage, int failuresInARow, double retryAfterSeconds);

        [LoggerMessage(Level = LogLevel.Debug, Message = "NetApiFlow: failed to persist trace {TraceId}")]
        public static partial void PersistenceFailureDetail(ILogger logger, string traceId, Exception exception);

        [LoggerMessage(Level = LogLevel.Information, Message = "NetApiFlow: the store saves traces again. {NotSavedTraces} traces were not saved while it was failing.")]
        public static partial void PersistenceRecovered(ILogger logger, long notSavedTraces);
    }
}
