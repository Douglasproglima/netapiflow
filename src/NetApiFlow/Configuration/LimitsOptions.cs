namespace NetApiFlow;

/// <summary>In-process limits that protect the host application from unbounded tracing overhead.</summary>
public sealed class LimitsOptions
{
    /// <summary>Maximum number of spans recorded per trace before extra spans are discarded. Default: 300.</summary>
    public int MaxSpansPerTrace { get; set; } = 300;

    /// <summary>Maximum log entries correlated per span (ADR-0017), before extra entries are
    /// discarded. Default: 20.</summary>
    public int MaxLogEntriesPerSpan { get; set; } = 20;
}
