namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /config</c>.</summary>
internal sealed record DashboardConfig
{
    public required long SlowThresholdMicros { get; init; }

    public required string Version { get; init; }

    public required bool TailSamplingEnabled { get; init; }

    public required double SuccessSampleRate { get; init; }

    public required string OpenApiDocumentPath { get; init; }

    /// <summary>The host's own <c>PathBase</c>, empty when it uses none. Separate from the
    /// dashboard's <c>&lt;base href&gt;</c>, which already combines it with the dashboard's own
    /// mount path.</summary>
    public required string HostPathBase { get; init; }
}
