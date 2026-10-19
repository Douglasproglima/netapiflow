using NetApiFlow.Domain;

namespace NetApiFlow;

/// <summary>Persists and queries traces. Implemented by the in-memory default store and by the optional storage packages.</summary>
public interface ITraceStore
{
    /// <summary>Saves a completed trace with its spans.</summary>
    ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken);

    /// <summary>Gets a consolidated trace by id, or <see langword="null"/> if not found.</summary>
    ValueTask<FlowTrace?> GetAsync(string traceId, CancellationToken cancellationToken);

    /// <summary>Lists traces matching <paramref name="query"/>, paginated.</summary>
    ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken);

    /// <summary>Aggregated statistics per component/operation within <paramref name="query"/>'s time window.</summary>
    ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken);

    /// <summary>Gets the last successful trace for an endpoint, used as the blueprint for "not executed" steps.</summary>
    ValueTask<FlowTrace?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken);

    /// <summary>Removes traces beyond <paramref name="policy"/>'s limits. Returns the number of traces removed.</summary>
    ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken);
}
