namespace NetApiFlow;

/// <summary>Time window for <see cref="ITraceStore.GetComponentStatsAsync"/>.</summary>
/// <remarks>Provisional shape. The authoritative REST contract is frozen in Fase 3, tarefa 3.1.</remarks>
public sealed record StatsQuery
{
    /// <summary>Only spans from traces started at or after this UTC timestamp.</summary>
    public DateTime? From { get; init; }

    /// <summary>Only spans from traces started at or before this UTC timestamp.</summary>
    public DateTime? To { get; init; }
}
