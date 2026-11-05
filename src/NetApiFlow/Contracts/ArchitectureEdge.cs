namespace NetApiFlow;

/// <summary>A distinct parent-child relationship observed between two spans of the same trace, part of an <see cref="ArchitectureMap"/>.</summary>
public sealed record ArchitectureEdge
{
    /// <summary>Component name of the calling step.</summary>
    public required string FromComponent { get; init; }

    /// <summary>Operation name of the calling step.</summary>
    public required string FromOperation { get; init; }

    /// <summary>Component name of the called step.</summary>
    public required string ToComponent { get; init; }

    /// <summary>Operation name of the called step.</summary>
    public required string ToOperation { get; init; }

    /// <summary>Number of times this relationship was observed, within the queried window.</summary>
    public required int CallCount { get; init; }
}
