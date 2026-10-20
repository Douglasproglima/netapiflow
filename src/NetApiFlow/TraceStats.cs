namespace NetApiFlow;

/// <summary>Aggregated statistics across whole traces (as opposed to <see cref="ComponentStat"/>, per component/operation).</summary>
public sealed record TraceStats
{
    /// <summary>Number of traces in the window.</summary>
    public required int TotalTraces { get; init; }

    /// <summary>Number of traces that ended in error.</summary>
    public required int ErrorCount { get; init; }

    /// <summary>Average duration, in microseconds.</summary>
    public required long AverageDurationMicros { get; init; }

    /// <summary>95th percentile duration, in microseconds.</summary>
    public required long P95DurationMicros { get; init; }

    /// <summary>Maximum duration observed, in microseconds.</summary>
    public required long MaxDurationMicros { get; init; }
}
