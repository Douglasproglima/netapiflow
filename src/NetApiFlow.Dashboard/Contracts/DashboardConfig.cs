namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /config</c> (D-24).</summary>
internal sealed record DashboardConfig
{
    public required long SlowThresholdMicros { get; init; }
}
