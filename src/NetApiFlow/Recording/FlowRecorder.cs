using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Sanitization;

namespace NetApiFlow.Recording;

/// <summary>Keeps in-memory state for traces that are still being recorded, within the limits of seção 6.7.</summary>
internal sealed class FlowRecorder
{
    /// <summary>Maximum number of traces tracked in memory at the same time.</summary>
    internal const int MaxActiveTraces = 500;

    private readonly ConcurrentDictionary<string, ActiveTrace> _activeTraces = new();
    private readonly NetApiFlowOptions _options;
    private long _droppedTraceCount;
    private long _droppedSpanCount;

    public FlowRecorder(IOptions<NetApiFlowOptions> options)
    {
        _options = options.Value;
    }

    public int ActiveTraceCount => _activeTraces.Count;

    public long DroppedTraceCount => Interlocked.Read(ref _droppedTraceCount);

    public long DroppedSpanCount => Interlocked.Read(ref _droppedSpanCount);

    /// <summary>Raised when a trace is opened.</summary>
    public event Action<FlowTrace>? TraceStarted;

    /// <summary>Raised when a trace is closed, with its final consolidated state.</summary>
    public event Action<FlowTrace>? TraceCompleted;

    /// <summary>Raised when a span is opened.</summary>
    public event Action<FlowSpan>? SpanStarted;

    /// <summary>Raised when a span closes without an error.</summary>
    public event Action<FlowSpan>? SpanCompleted;

    /// <summary>Raised when a span closes in an error status.</summary>
    public event Action<FlowSpan>? SpanFailed;

    /// <summary>Raised when a trace closes, with its final state and spans, for the persistence writer.</summary>
    public event Action<FlowTrace, IReadOnlyList<FlowSpan>>? TraceReadyForPersistence;

    public bool TryStartTrace(string traceId, string method, string routeTemplate, DateTime startedAtUtc)
    {
        if (_activeTraces.Count >= MaxActiveTraces)
        {
            Interlocked.Increment(ref _droppedTraceCount);
            FlowMetrics.DroppedTraces.Add(1);
            return false;
        }

        if (!_activeTraces.TryAdd(traceId, new ActiveTrace(traceId, method, routeTemplate, startedAtUtc)))
        {
            return false;
        }

        TraceStarted?.Invoke(new FlowTrace
        {
            TraceId = traceId,
            Method = method,
            RouteTemplate = routeTemplate,
            Status = FlowStatus.Running,
            StartedAt = startedAtUtc,
        });

        return true;
    }

    /// <summary>Updates the route template for an open trace, once routing has resolved the matched endpoint.</summary>
    public void UpdateRouteTemplate(string traceId, string routeTemplate)
    {
        if (_activeTraces.TryGetValue(traceId, out var trace))
        {
            trace.RouteTemplate = routeTemplate;
        }
    }

    /// <summary>Closes an open trace and returns its final consolidated state, or <see langword="null"/> if it was not tracked.</summary>
    public FlowTrace? CompleteTrace(string traceId, int? httpStatus, FlowStatus status, string? errorMessage)
    {
        if (!TryCompleteTrace(traceId, out var trace) || trace is null)
        {
            return null;
        }

        var endedAtUtc = DateTime.UtcNow;
        var result = new FlowTrace
        {
            TraceId = trace.TraceId,
            Method = trace.Method,
            RouteTemplate = trace.RouteTemplate,
            HttpStatus = httpStatus,
            Status = status,
            StartedAt = trace.StartedAtUtc,
            EndedAt = endedAtUtc,
            DurationMicros = (long)(endedAtUtc - trace.StartedAtUtc).TotalMicroseconds,
            SpanCount = trace.Spans.Count,
            Truncated = trace.Truncated,
            ErrorMessage = ErrorMessageSanitizer.Sanitize(errorMessage),
        };

        TraceCompleted?.Invoke(result);
        TraceReadyForPersistence?.Invoke(result, trace.Spans);
        return result;
    }

    public bool TryAddSpan(string traceId, FlowSpan span)
    {
        if (!_activeTraces.TryGetValue(traceId, out var trace))
        {
            return false;
        }

        if (trace.TryAddSpan(span, _options.Limits.MaxSpansPerTrace))
        {
            SpanStarted?.Invoke(span);
            return true;
        }

        Interlocked.Increment(ref _droppedSpanCount);
        FlowMetrics.DroppedSpans.Add(1);
        return false;
    }

    /// <summary>Updates a span in place without publishing an event. Used to propagate an error status to a still-running ancestor.</summary>
    public void UpdateSpan(string traceId, FlowSpan span)
    {
        if (_activeTraces.TryGetValue(traceId, out var trace))
        {
            trace.ReplaceSpan(span);
        }
    }

    /// <summary>Records a span's final state and publishes <see cref="SpanCompleted"/> or <see cref="SpanFailed"/>.</summary>
    public void CompleteSpan(string traceId, FlowSpan span)
    {
        if (!_activeTraces.TryGetValue(traceId, out var trace))
        {
            return;
        }

        trace.ReplaceSpan(span);

        if (span.Status == FlowStatus.Error)
        {
            SpanFailed?.Invoke(span);
        }
        else
        {
            SpanCompleted?.Invoke(span);
        }
    }

    public ActiveTrace? GetActiveTrace(string traceId) =>
        _activeTraces.TryGetValue(traceId, out var trace) ? trace : null;

    public bool TryCompleteTrace(string traceId, out ActiveTrace? trace) =>
        _activeTraces.TryRemove(traceId, out trace);
}
