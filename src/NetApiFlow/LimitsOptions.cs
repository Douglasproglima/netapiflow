namespace NetApiFlow;

/// <summary>In-process limits that protect the host application from unbounded tracing overhead.</summary>
public sealed class LimitsOptions
{
    /// <summary>Maximum number of spans recorded per trace before extra spans are discarded. Default: 300.</summary>
    public int MaxSpansPerTrace { get; set; } = 300;
}
