namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /config</c> (D-24, <c>version</c> added by D-33).</summary>
internal sealed record DashboardConfig
{
    public required long SlowThresholdMicros { get; init; }

    public required string Version { get; init; }
}
