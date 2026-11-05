namespace NetApiFlow;

/// <summary>Time window for <see cref="ITraceStore.GetComponentStatsAsync"/>.</summary>
public sealed record StatsQuery
{
    /// <summary>Only spans from traces started at or after this UTC timestamp.</summary>
    public DateTime? From { get; init; }

    /// <summary>Only spans from traces started at or before this UTC timestamp.</summary>
    public DateTime? To { get; init; }
}
