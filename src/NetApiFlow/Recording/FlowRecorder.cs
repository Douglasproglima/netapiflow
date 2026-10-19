using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;

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

    public bool TryStartTrace(string traceId, string method, string routeTemplate, DateTime startedAtUtc)
    {
        if (_activeTraces.Count >= MaxActiveTraces)
        {
            Interlocked.Increment(ref _droppedTraceCount);
            return false;
        }

        return _activeTraces.TryAdd(traceId, new ActiveTrace(traceId, method, routeTemplate, startedAtUtc));
    }

    public bool TryAddSpan(string traceId, FlowSpan span)
    {
        if (!_activeTraces.TryGetValue(traceId, out var trace))
        {
            return false;
        }

        if (trace.TryAddSpan(span, _options.Limits.MaxSpansPerTrace))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedSpanCount);
        return false;
    }

    public void UpdateSpan(string traceId, FlowSpan span)
    {
        if (_activeTraces.TryGetValue(traceId, out var trace))
        {
            trace.ReplaceSpan(span);
        }
    }

    public ActiveTrace? GetActiveTrace(string traceId) =>
        _activeTraces.TryGetValue(traceId, out var trace) ? trace : null;

    public bool TryCompleteTrace(string traceId, out ActiveTrace? trace) =>
        _activeTraces.TryRemove(traceId, out trace);
}
