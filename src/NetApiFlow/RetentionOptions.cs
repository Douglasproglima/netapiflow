namespace NetApiFlow;

/// <summary>Retention policy for persisted traces.</summary>
public sealed class RetentionOptions
{
    /// <summary>Maximum number of traces kept in the store. Default: 1000.</summary>
    public int MaxTraces { get; set; } = 1000;

    /// <summary>Maximum age of a trace before it is eligible for removal. Default: 24 hours.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
}
