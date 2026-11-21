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
                ReportRecoveryIfWaiting();
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
        Interlocked.Increment(ref _failureCount);
        FlowMetrics.PersistenceFailures.Add(1);
        _retryAfter = _clock.GetUtcNow() + _retryInterval;
        Log.PersistenceFailed(_logger, traceId, exception.GetType().Name, exception.Message, _retryInterval.TotalSeconds);
        Log.PersistenceFailureDetail(_logger, traceId, exception);
    }

    private void ReportRecoveryIfWaiting()
    {
        if (_retryAfter is null)
        {
            return;
        }

        _retryAfter = null;
        var notSaved = _notSavedSinceTheFirstFailure;
        _notSavedSinceTheFirstFailure = 0;
        Log.PersistenceRecovered(_logger, notSaved);
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
            Message = "NetApiFlow: the store failed to save trace {TraceId} ({ExceptionType}: {ExceptionMessage}). Traces are discarded for {RetryAfterSeconds} seconds and then tried again. The count is netapiflow.persistence.failures.")]
        public static partial void PersistenceFailed(ILogger logger, string traceId, string exceptionType, string exceptionMessage, double retryAfterSeconds);

        [LoggerMessage(Level = LogLevel.Debug, Message = "NetApiFlow: failed to persist trace {TraceId}")]
        public static partial void PersistenceFailureDetail(ILogger logger, string traceId, Exception exception);

        [LoggerMessage(Level = LogLevel.Information, Message = "NetApiFlow: the store saves traces again. {NotSavedTraces} traces were not saved while it was failing.")]
        public static partial void PersistenceRecovered(ILogger logger, long notSavedTraces);
    }
}
