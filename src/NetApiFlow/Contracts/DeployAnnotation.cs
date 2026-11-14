namespace NetApiFlow;

/// <summary>A deploy marker shown alongside execution history, shared across everyone who opens the dashboard.</summary>
public sealed record DeployAnnotation
{
    /// <summary>Unique identifier, assigned by the store on save.</summary>
    public required string Id { get; init; }

    /// <summary>When the deploy happened, always assigned by the server, never by the caller.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Free-text label identifying the deploy, e.g. a version or release name.</summary>
    public required string Label { get; init; }
}
