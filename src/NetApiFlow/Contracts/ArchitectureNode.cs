namespace NetApiFlow;

/// <summary>A distinct component/operation step observed while executing a route, part of an <see cref="ArchitectureMap"/>.</summary>
public sealed record ArchitectureNode
{
    /// <summary>Component name.</summary>
    public required string Component { get; init; }

    /// <summary>Operation name.</summary>
    public required string Operation { get; init; }

    /// <summary>Kind of component this node represents.</summary>
    public required ComponentType Type { get; init; }

    /// <summary>Number of executions of the route, within the queried window, that passed through this step.</summary>
    public required int ExecutionCount { get; init; }
}
