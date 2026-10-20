using NetApiFlow.Domain;

namespace NetApiFlow.Recording;

/// <summary>Mutable, thread-safe state for a trace that is still being recorded.</summary>
internal sealed class ActiveTrace(string traceId, string method, string routeTemplate, DateTime startedAtUtc)
{
    private readonly object _lock = new();
    private readonly List<FlowSpan> _spans = [];
    private IReadOnlyDictionary<string, string> _tags = EmptyTags;
    private int _nextSequence;

    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    public string TraceId { get; } = traceId;

    public string Method { get; } = method;

    public string RouteTemplate { get; set; } = routeTemplate;

    public DateTime StartedAtUtc { get; } = startedAtUtc;

    public bool Truncated { get; private set; }

    public IReadOnlyDictionary<string, string> Tags
    {
        get
        {
            lock (_lock)
            {
                return _tags;
            }
        }
    }

    public void SetTag(string key, string value)
    {
        lock (_lock)
        {
            _tags = new Dictionary<string, string>(_tags) { [key] = value };
        }
    }

    public IReadOnlyList<FlowSpan> Spans
    {
        get
        {
            lock (_lock)
            {
                return [.. _spans];
            }
        }
    }

    public int NextSequence() => Interlocked.Increment(ref _nextSequence);

    public bool TryAddSpan(FlowSpan span, int maxSpansPerTrace)
    {
        lock (_lock)
        {
            if (_spans.Count >= maxSpansPerTrace)
            {
                Truncated = true;
                return false;
            }

            _spans.Add(span);
            return true;
        }
    }

    public void ReplaceSpan(FlowSpan updated)
    {
        lock (_lock)
        {
            var index = _spans.FindIndex(s => s.SpanId == updated.SpanId);
            if (index >= 0)
            {
                _spans[index] = updated;
            }
        }
    }
}
