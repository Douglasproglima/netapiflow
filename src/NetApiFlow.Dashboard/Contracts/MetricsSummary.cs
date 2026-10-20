namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /metrics</c>.</summary>
internal sealed record MetricsSummary
{
    public required int TotalTraces { get; init; }

    public required double ErrorRate { get; init; }

    public required long AverageDurationMicros { get; init; }

    public required long P95DurationMicros { get; init; }

    public required long MaxDurationMicros { get; init; }

    public static MetricsSummary From(TraceStats stats) => new()
    {
        TotalTraces = stats.TotalTraces,
        ErrorRate = stats.TotalTraces == 0 ? 0 : (double)stats.ErrorCount / stats.TotalTraces,
        AverageDurationMicros = stats.AverageDurationMicros,
        P95DurationMicros = stats.P95DurationMicros,
        MaxDurationMicros = stats.MaxDurationMicros,
    };
}
