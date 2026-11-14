namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of the <c>POST /deploys</c> request body.</summary>
internal sealed record CreateDeployAnnotationRequest
{
    public required string Label { get; init; }
}
