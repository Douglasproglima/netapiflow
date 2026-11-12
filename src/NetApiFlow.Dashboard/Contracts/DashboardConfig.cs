namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /config</c>.</summary>
internal sealed record DashboardConfig
{
    public required long SlowThresholdMicros { get; init; }

    public required string Version { get; init; }

    public required bool TailSamplingEnabled { get; init; }

    public required double SuccessSampleRate { get; init; }

    public required string OpenApiDocumentPath { get; init; }
}
