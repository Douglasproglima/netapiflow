namespace NetApiFlow;

/// <summary>Aggregated statistics for an HTTP method/route template pair, across stored traces.</summary>
public sealed record RouteStat
{
    /// <summary>HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>Route template.</summary>
    public required string RouteTemplate { get; init; }

    /// <summary>Number of traces recorded for this route.</summary>
    public required int ExecutionCount { get; init; }

    /// <summary>Number of traces that ended in error.</summary>
    public required int ErrorCount { get; init; }

    /// <summary>Median duration, in microseconds.</summary>
    public required long MedianDurationMicros { get; init; }

    /// <summary>Average duration, in microseconds.</summary>
    public required long AverageDurationMicros { get; init; }

    /// <summary>95th percentile duration, in microseconds.</summary>
    public required long P95DurationMicros { get; init; }

    /// <summary>Maximum duration observed, in microseconds.</summary>
    public required long MaxDurationMicros { get; init; }
}
