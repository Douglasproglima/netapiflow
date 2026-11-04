using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.Mcp;

/// <summary>Registers the MCP server. See <c>UseNetApiFlowMcp</c> in <c>NetApiFlow.Mcp.Endpoints</c> to mount it.</summary>
public static class NetApiFlowMcpServiceCollectionExtensions
{
    /// <summary>Registers the MCP server with HTTP transport and the tools declared in this assembly.</summary>
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
