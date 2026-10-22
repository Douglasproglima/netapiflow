namespace NetApiFlow;

/// <summary>Aggregated statistics for a component/operation pair, across stored traces.</summary>
/// <remarks>Provisional shape. The authoritative REST contract is frozen in Fase 3, tarefa 3.1.</remarks>
public sealed record ComponentStat
{
    /// <summary>Component name.</summary>
    public required string Component { get; init; }

    /// <summary>Operation name.</summary>
    public required string Operation { get; init; }

    /// <summary>Number of spans recorded.</summary>
    public required int ExecutionCount { get; init; }

    /// <summary>Number of spans that ended in error.</summary>
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
