namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /config</c> (D-24, <c>version</c> added by D-33,
/// <c>tailSamplingEnabled</c>/<c>successSampleRate</c> added by D-82).</summary>
internal sealed record DashboardConfig
{
    public required long SlowThresholdMicros { get; init; }

    public required string Version { get; init; }

    public required bool TailSamplingEnabled { get; init; }

    public required double SuccessSampleRate { get; init; }
}
