namespace NetApiFlow;

/// <summary>
/// Static graph of a route (<c>method</c>, <c>routeTemplate</c>), built from the union of every
/// execution recorded within a <see cref="StatsQuery"/> window. See <see cref="ITraceStore.GetArchitectureMapAsync"/>.
/// </summary>
public sealed record ArchitectureMap
{
    /// <summary>Total number of executions of the route within the queried window.</summary>
    public required int TotalExecutions { get; init; }

    /// <summary>Every distinct component/operation step observed.</summary>
    public required IReadOnlyList<ArchitectureNode> Nodes { get; init; }

    /// <summary>Every distinct parent-child relationship observed between steps.</summary>
    public required IReadOnlyList<ArchitectureEdge> Edges { get; init; }
}
