using System.Collections.Concurrent;
using NetApiFlow.Domain;

namespace NetApiFlow.Storage;

/// <summary>Default <see cref="ITraceStore"/>: keeps every trace and its spans in memory until retention removes it.</summary>
internal sealed class InMemoryTraceStore : ITraceStore
{
    private sealed record Entry(FlowTrace Trace, IReadOnlyList<FlowSpan> Spans);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken)
    {
        _entries[trace.TraceId] = new Entry(trace, spans);
        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_entries.TryGetValue(traceId, out var entry) ? ToDetail(entry) : null);

    public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken)
    {
        var matching = _entries.Values
            .Select(e => e.Trace)
            .Where(trace => query.Status is null || trace.Status == query.Status)
            .Where(trace => query.Method is null || string.Equals(trace.Method, query.Method, StringComparison.OrdinalIgnoreCase))
            .Where(trace => query.RouteTemplate is null || trace.RouteTemplate == query.RouteTemplate)
            .Where(trace => query.Tenant is null || trace.Tags.GetValueOrDefault("tenant") == query.Tenant)
            .Where(trace => query.From is null || trace.StartedAt >= query.From)
            .Where(trace => query.To is null || trace.StartedAt <= query.To)
            .Where(trace => query.MinDurationMicros is null || (trace.DurationMicros ?? 0) >= query.MinDurationMicros)
            .ToList();

        matching = ApplySort(matching, query);

        var page = matching
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(trace => new FlowTraceSummary
            {
                TraceId = trace.TraceId,
                Method = trace.Method,
                RouteTemplate = trace.RouteTemplate,
                HttpStatus = trace.HttpStatus,
                Status = trace.Status,
                StartedAt = trace.StartedAt,
                DurationMicros = trace.DurationMicros,
                Truncated = trace.Truncated,
            })
            .ToList();

        return ValueTask.FromResult(new PagedResult<FlowTraceSummary>
        {
            Items = page,
            TotalCount = matching.Count,
        });
    }

    public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        var spans = _entries.Values
            .Where(e => query.From is null || e.Trace.StartedAt >= query.From)
            .Where(e => query.To is null || e.Trace.StartedAt <= query.To)
            .SelectMany(e => e.Spans)
            .GroupBy(span => (span.Component, span.Operation));

        var stats = spans.Select(group =>
        {
            var durations = group
                .Where(s => s.DurationMicros is not null)
                .Select(s => s.DurationMicros!.Value)
                .OrderBy(d => d)
                .ToList();

            return new ComponentStat
            {
                Component = group.Key.Component,
                Operation = group.Key.Operation,
                ExecutionCount = group.Count(),
                ErrorCount = group.Count(s => s.Status == FlowStatus.Error),
                MedianDurationMicros = Percentile(durations, 0.5),
                AverageDurationMicros = durations.Count == 0 ? 0 : (long)durations.Average(),
                P95DurationMicros = Percentile(durations, 0.95),
                MaxDurationMicros = durations.Count == 0 ? 0 : durations[^1],
            };
        }).ToList();

        return ValueTask.FromResult<IReadOnlyList<ComponentStat>>(stats);
    }

    public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken)
    {
        var entry = _entries.Values
            .Where(e => string.Equals(e.Trace.Method, method, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Trace.RouteTemplate == routeTemplate)
            .Where(e => e.Trace.Status == FlowStatus.Success)
            .OrderByDescending(e => e.Trace.StartedAt)
            .FirstOrDefault();

        return ValueTask.FromResult(entry is null ? null : ToDetail(entry));
    }

    public ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        var traces = _entries.Values
            .Select(e => e.Trace)
            .Where(trace => query.From is null || trace.StartedAt >= query.From)
            .Where(trace => query.To is null || trace.StartedAt <= query.To)
            .ToList();

        var durations = traces
            .Where(t => t.DurationMicros is not null)
            .Select(t => t.DurationMicros!.Value)
            .OrderBy(d => d)
            .ToList();

        return ValueTask.FromResult(new TraceStats
        {
            TotalTraces = traces.Count,
            ErrorCount = traces.Count(t => t.Status == FlowStatus.Error),
            AverageDurationMicros = durations.Count == 0 ? 0 : (long)durations.Average(),
            P95DurationMicros = Percentile(durations, 0.95),
            MaxDurationMicros = durations.Count == 0 ? 0 : durations[^1],
        });
    }

    public ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        var traces = _entries.Values
            .Select(e => e.Trace)
            .Where(trace => query.From is null || trace.StartedAt >= query.From)
            .Where(trace => query.To is null || trace.StartedAt <= query.To)
            .GroupBy(trace => (trace.Method, trace.RouteTemplate));

        var stats = traces.Select(group =>
        {
            var durations = group
                .Where(t => t.DurationMicros is not null)
                .Select(t => t.DurationMicros!.Value)
                .OrderBy(d => d)
                .ToList();

            return new RouteStat
            {
                Method = group.Key.Method,
                RouteTemplate = group.Key.RouteTemplate,
                ExecutionCount = group.Count(),
                ErrorCount = group.Count(t => t.Status == FlowStatus.Error),
                MedianDurationMicros = Percentile(durations, 0.5),
                AverageDurationMicros = durations.Count == 0 ? 0 : (long)durations.Average(),
                P95DurationMicros = Percentile(durations, 0.95),
                MaxDurationMicros = durations.Count == 0 ? 0 : durations[^1],
            };
        }).ToList();

        return ValueTask.FromResult<IReadOnlyList<RouteStat>>(stats);
    }

    public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeTemplate, StatsQuery query, CancellationToken cancellationToken)
    {
        var entries = _entries.Values
            .Where(e => string.Equals(e.Trace.Method, method, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Trace.RouteTemplate == routeTemplate)
            .Where(e => query.From is null || e.Trace.StartedAt >= query.From)
            .Where(e => query.To is null || e.Trace.StartedAt <= query.To)
            .ToList();

        var nodeTypes = new Dictionary<(string Component, string Operation), ComponentType>();
        var nodeExecutionCounts = new Dictionary<(string Component, string Operation), int>();
        var edgeCallCounts = new Dictionary<(string FromComponent, string FromOperation, string ToComponent, string ToOperation), int>();

        foreach (var entry in entries)
        {
            var spansBySpanId = entry.Spans.ToDictionary(s => s.SpanId);
            var nodesInTrace = new HashSet<(string Component, string Operation)>();

            foreach (var span in entry.Spans)
            {
                var nodeKey = (span.Component, span.Operation);
                if (nodesInTrace.Add(nodeKey))
                {
                    nodeTypes[nodeKey] = span.Type;
                    nodeExecutionCounts[nodeKey] = nodeExecutionCounts.GetValueOrDefault(nodeKey) + 1;
                }

                if (span.ParentSpanId is not null && spansBySpanId.TryGetValue(span.ParentSpanId, out var parent))
                {
                    var edgeKey = (parent.Component, parent.Operation, span.Component, span.Operation);
                    edgeCallCounts[edgeKey] = edgeCallCounts.GetValueOrDefault(edgeKey) + 1;
                }
            }
        }

        var nodes = nodeExecutionCounts
            .Select(kvp => new ArchitectureNode
            {
                Component = kvp.Key.Component,
                Operation = kvp.Key.Operation,
                Type = nodeTypes[kvp.Key],
                ExecutionCount = kvp.Value,
            })
            .ToList<ArchitectureNode>();

        var edges = edgeCallCounts
            .Select(kvp => new ArchitectureEdge
            {
                FromComponent = kvp.Key.FromComponent,
                FromOperation = kvp.Key.FromOperation,
                ToComponent = kvp.Key.ToComponent,
                ToOperation = kvp.Key.ToOperation,
                CallCount = kvp.Value,
            })
            .ToList<ArchitectureEdge>();

        return ValueTask.FromResult(new ArchitectureMap
        {
            TotalExecutions = entries.Count,
            Nodes = nodes,
            Edges = edges,
        });
    }

    public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - policy.MaxAge;
        var totalCount = _entries.Count;
        var removed = 0;

        foreach (var (traceId, entry) in _entries.OrderBy(kvp => kvp.Value.Trace.StartedAt).ToList())
        {
            var exceedsAge = entry.Trace.StartedAt < cutoff;
            var exceedsCount = totalCount - removed > policy.MaxTraces;

            if (exceedsAge || exceedsCount)
            {
                if (_entries.TryRemove(traceId, out _))
                {
                    removed++;
                }
            }
        }

        return ValueTask.FromResult(removed);
    }

    private static List<FlowTrace> ApplySort(List<FlowTrace> traces, TraceQuery query)
    {
        var field = query.SortBy ?? TraceSortField.StartedAt;
        var ascending = query.SortDirection == global::NetApiFlow.SortDirection.Ascending;

        IOrderedEnumerable<FlowTrace> ordered = field switch
        {
            TraceSortField.Duration => OrderByNullsLast(traces, t => t.DurationMicros, ascending),
            TraceSortField.HttpStatus => OrderByNullsLast(traces, t => t.HttpStatus, ascending),
            TraceSortField.Method => ascending
                ? traces.OrderBy(t => t.Method, StringComparer.OrdinalIgnoreCase)
                : traces.OrderByDescending(t => t.Method, StringComparer.OrdinalIgnoreCase),
            TraceSortField.Route => ascending
                ? traces.OrderBy(t => t.RouteTemplate, StringComparer.OrdinalIgnoreCase)
                : traces.OrderByDescending(t => t.RouteTemplate, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? traces.OrderBy(t => t.StartedAt)
                : traces.OrderByDescending(t => t.StartedAt),
        };

        if (field != TraceSortField.StartedAt)
        {
            ordered = ordered.ThenByDescending(t => t.StartedAt);
        }

        return ordered.ThenBy(t => t.TraceId, StringComparer.Ordinal).ToList();
    }

    private static IOrderedEnumerable<FlowTrace> OrderByNullsLast<TKey>(
        List<FlowTrace> traces, Func<FlowTrace, TKey?> selector, bool ascending)
        where TKey : struct, IComparable<TKey>
    {
        var byPresence = traces.OrderBy(t => selector(t) is null);
        return ascending ? byPresence.ThenBy(selector) : byPresence.ThenByDescending(selector);
    }

    private static FlowTraceDetail ToDetail(Entry entry) =>
        new() { Trace = entry.Trace, Spans = entry.Spans };

    private static long Percentile(List<long> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}
