namespace NetApiFlow;

/// <summary>Field a <see cref="TraceQuery"/> sorts <see cref="ITraceStore.QueryAsync"/> results by.</summary>
public enum TraceSortField
{
    /// <summary>Sorts by <see cref="Domain.FlowTrace.StartedAt"/>.</summary>
    StartedAt,

    /// <summary>Sorts by <see cref="Domain.FlowTrace.DurationMicros"/>.</summary>
    Duration,

    /// <summary>Sorts by <see cref="Domain.FlowTrace.HttpStatus"/>.</summary>
    HttpStatus,

    /// <summary>Sorts by <see cref="Domain.FlowTrace.Method"/>.</summary>
    Method,

    /// <summary>Sorts by <see cref="Domain.FlowTrace.RouteTemplate"/>, ordinal, case-insensitive.</summary>
    Route,
}
