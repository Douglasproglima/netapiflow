using NetApiFlow.Domain;

namespace NetApiFlow;

/// <summary>Persists and queries traces. Implemented by the in-memory default store and by the optional storage packages.</summary>
public interface ITraceStore
{
    /// <summary>Saves a completed trace with its spans.</summary>
    ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken);

    /// <summary>Gets a consolidated trace with its spans by id, or <see langword="null"/> if not found.</summary>
    ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken);

    /// <summary>Lists traces matching <paramref name="query"/>, paginated.</summary>
    ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken);

    /// <summary>Aggregated statistics per component/operation within <paramref name="query"/>'s time window.</summary>
    ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken);

    /// <summary>Aggregated statistics across whole traces within <paramref name="query"/>'s time window.</summary>
    ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken);

    /// <summary>Aggregated statistics per HTTP method/route template within <paramref name="query"/>'s time window.</summary>
    ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Static graph of an endpoint, built from the union of every execution within
    /// <paramref name="query"/>'s time window. <paramref name="routeKey"/> is matched with every
    /// recorded trace's route template reduced to the same parameter-constraint-free shape, so a
    /// caller without access to parameter constraints (e.g. an OpenAPI document) still finds the
    /// endpoint.
    /// </summary>
    ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeKey, StatsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the last successful trace with its spans for an endpoint, used as the blueprint for
    /// "not executed" steps. <paramref name="routeKey"/> is matched with every recorded trace's
    /// route template reduced to the same parameter-constraint-free shape, so a caller without
    /// access to parameter constraints (e.g. an OpenAPI document) still finds the endpoint.
    /// </summary>
    ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the last successful trace of an endpoint that started strictly before
    /// <paramref name="before"/>, with its spans. Used as the blueprint of a trace that started at
    /// <paramref name="before"/>, so the trace is never its own blueprint and a later execution never
    /// changes the drawing of an earlier one. <paramref name="routeKey"/> is matched as in
    /// <see cref="GetLastSuccessfulAsync(string, string, CancellationToken)"/>. The default
    /// implementation asks for the latest successful trace and returns it only when it started before
    /// <paramref name="before"/>, so it never returns a wrong trace and may return <see langword="null"/>
    /// where an exact implementation would find an earlier one.
    /// </summary>
    async ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, DateTime before, CancellationToken cancellationToken)
    {
        var latest = await GetLastSuccessfulAsync(method, routeKey, cancellationToken);
        return latest is not null && latest.Trace.StartedAt < before ? latest : null;
    }

    /// <summary>Removes traces beyond <paramref name="policy"/>'s limits. Returns the number of traces removed.</summary>
    ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken);
}
