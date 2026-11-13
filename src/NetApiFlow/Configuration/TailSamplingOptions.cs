namespace NetApiFlow;

/// <summary>Tail-based sampling of persisted traces (D-82). Default: disabled, every completed
/// trace is persisted, same behavior as before this option existed.</summary>
public sealed class TailSamplingOptions
{
    /// <summary>Enables tail-based sampling. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Fraction (0.0-1.0) of successful, non-slow traces to persist. A trace with
    /// <see cref="FlowStatus.Error"/> or <see cref="FlowStatus.Timeout"/>, or a duration at or
    /// above <see cref="NetApiFlowOptions.SlowThreshold"/>, is always persisted, regardless of
    /// this value. A <see cref="FlowStatus.Cancelled"/> trace follows the same sampling rate as
    /// <see cref="FlowStatus.Success"/> (ADR-0020): a client disconnecting is routine churn, not
    /// a server-side problem worth guaranteeing coverage for. Default: <c>0.1</c> (keep 10%).</summary>
    public double SuccessSampleRate { get; set; } = 0.1;
}
