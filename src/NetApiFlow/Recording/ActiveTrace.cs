using NetApiFlow.Domain;

namespace NetApiFlow.Recording;

/// <summary>Mutable, thread-safe state for a trace that is still being recorded.</summary>
internal sealed class ActiveTrace(string traceId, string method, string routeTemplate, DateTime startedAtUtc, bool published = true)
{
    private readonly object _lock = new();
    private readonly List<FlowSpan> _spans = [];
    private IReadOnlyDictionary<string, string> _tags = EmptyTags;
    private int _nextSequence;
    private bool _published = published;

    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    public string TraceId { get; } = traceId;

    public string Method { get; } = method;

    public string RouteTemplate { get; set; } = routeTemplate;

    public DateTime StartedAtUtc { get; } = startedAtUtc;

    public bool Truncated { get; private set; }

    public bool Published
    {
        get
        {
            lock (_lock)
            {
                return _published;
            }
        }
    }

    public IReadOnlyList<FlowSpan>? Publish(string routeTemplate)
    {
        lock (_lock)
        {
            if (_published)
            {
                return null;
            }

            RouteTemplate = routeTemplate;
            _published = true;
            return [.. _spans];
        }
    }

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

    public bool TryAddSpan(FlowSpan span, int maxSpansPerTrace, out bool published)
    {
        lock (_lock)
        {
            published = _published;
            if (_spans.Count >= maxSpansPerTrace)
            {
                Truncated = true;
                return false;
            }

            _spans.Add(span);
            return true;
        }
    }

    public bool ReplaceSpan(FlowSpan updated)
    {
        lock (_lock)
        {
            var index = _spans.FindIndex(s => s.SpanId == updated.SpanId);
            if (index >= 0)
            {
                _spans[index] = updated;
            }

            return _published;
        }
    }

    public bool TryAddLogEntry(string spanId, LogEntry entry, int maxLogEntriesPerSpan)
    {
        lock (_lock)
        {
            var index = _spans.FindIndex(s => s.SpanId == spanId);
            if (index < 0 || _spans[index].Logs.Count >= maxLogEntriesPerSpan)
            {
                return false;
            }

            _spans[index] = _spans[index] with { Logs = [.. _spans[index].Logs, entry] };
            return true;
        }
    }
}
