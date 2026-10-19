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

    public ValueTask<FlowTrace?> GetAsync(string traceId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_entries.TryGetValue(traceId, out var entry) ? entry.Trace : null);

    public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken)
    {
        var matching = _entries.Values
            .Select(e => e.Trace)
            .Where(trace => query.Status is null || trace.Status == query.Status)
            .Where(trace => query.Method is null || string.Equals(trace.Method, query.Method, StringComparison.OrdinalIgnoreCase))
            .Where(trace => query.RouteTemplate is null || trace.RouteTemplate == query.RouteTemplate)
            .Where(trace => query.From is null || trace.StartedAt >= query.From)
            .Where(trace => query.To is null || trace.StartedAt <= query.To)
            .Where(trace => query.MinDurationMicros is null || (trace.DurationMicros ?? 0) >= query.MinDurationMicros)
            .OrderByDescending(trace => trace.StartedAt)
            .ToList();

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

    public ValueTask<FlowTrace?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken)
    {
        var trace = _entries.Values
            .Select(e => e.Trace)
            .Where(t => string.Equals(t.Method, method, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.RouteTemplate == routeTemplate)
            .Where(t => t.Status == FlowStatus.Success)
            .OrderByDescending(t => t.StartedAt)
            .FirstOrDefault();

        return ValueTask.FromResult(trace);
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
