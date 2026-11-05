namespace NetApiFlow;

/// <summary>Retention limits applied by <see cref="ITraceStore.ApplyRetentionAsync"/>.</summary>
public sealed record RetentionPolicy
{
    /// <summary>Maximum number of traces kept in the store.</summary>
    public required int MaxTraces { get; init; }

    /// <summary>Maximum age of a trace before it is eligible for removal.</summary>
    public required TimeSpan MaxAge { get; init; }
}
