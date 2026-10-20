using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace NetApiFlow.Dashboard.Endpoints;

/// <summary>
/// Applies a convention (such as <c>RequireAuthorization</c>) to every endpoint group that makes
/// up the dashboard (API/SSE and static UI), since they are mapped as two separate route groups.
/// </summary>
internal sealed class CompositeEndpointConventionBuilder(params IEndpointConventionBuilder[] builders)
    : IEndpointConventionBuilder
{
    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var builder in builders)
        {
            builder.Add(convention);
        }
    }
}
