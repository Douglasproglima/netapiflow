using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NetApiFlow.Mcp.Endpoints;

/// <summary>Mounts the MCP server's HTTP transport.</summary>
public static class NetApiFlowMcpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Serves the MCP server at <paramref name="basePath"/> (default <c>/flow/mcp</c>). Chain
    /// <c>RequireAuthorization</c> on the result to protect it with a policy. Outside
    /// Development, without an <see cref="IFlowAccessFilter"/>, every route answers 404 unless it
    /// requires authorization and does not allow anonymous access: chaining
    /// <c>AllowAnonymous</c> together with <c>RequireAuthorization</c> authorizes nothing.
    /// </summary>
    /// <param name="endpoints">The route builder to add the MCP server to.</param>
    /// <param name="basePath">The path the MCP server is served at.</param>
    /// <returns>A builder that applies a convention to the MCP routes.</returns>
    public static IEndpointConventionBuilder MapNetApiFlowMcp(this IEndpointRouteBuilder endpoints, string basePath = "/flow/mcp")
    {
        var group = endpoints.MapMcp(basePath);
        group.AddEndpointFilter(McpAccessGate.EnforceAsync);
        group.ExcludeFromDescription();
        group.WithoutNetApiFlowTracking();
        return group;
    }

    /// <summary>The former name of <see cref="MapNetApiFlowMcp"/>, which it forwards to.</summary>
    /// <param name="endpoints">The route builder to add the MCP server to.</param>
    /// <param name="basePath">The path the MCP server is served at.</param>
    /// <returns>A builder that applies a convention to the MCP routes.</returns>
    [Obsolete("Use MapNetApiFlowMcp.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IEndpointConventionBuilder UseNetApiFlowMcp(this IEndpointRouteBuilder endpoints, string basePath = "/flow/mcp") =>
        endpoints.MapNetApiFlowMcp(basePath);
}
