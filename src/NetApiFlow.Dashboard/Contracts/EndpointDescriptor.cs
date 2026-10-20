namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of a <c>GET /endpoints</c> list item.</summary>
internal sealed record EndpointDescriptor
{
    public required string Method { get; init; }

    public required string RouteTemplate { get; init; }

    public required string DisplayName { get; init; }
}
