using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NetApiFlow.Mcp.Endpoints;

/// <summary>Mounts the MCP server's HTTP transport.</summary>
public static class NetApiFlowMcpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Serves the MCP server at <paramref name="basePath"/> (default <c>/flow/mcp</c>). Chain
    /// <c>RequireAuthorization</c> on the result to protect it with a policy (risco R-14).
    /// </summary>
    public static IEndpointConventionBuilder UseNetApiFlowMcp(this IEndpointRouteBuilder endpoints, string basePath = "/flow/mcp")
    {
        var group = endpoints.MapMcp(basePath);
        group.AddEndpointFilter(McpAccessGate.EnforceAsync);
        return group;
    }
}
