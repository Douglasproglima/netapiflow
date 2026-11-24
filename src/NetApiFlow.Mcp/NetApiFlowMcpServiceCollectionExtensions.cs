using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace NetApiFlow.Mcp;

/// <summary>Registers the MCP server. See <c>MapNetApiFlowMcp</c> in <c>NetApiFlow.Mcp.Endpoints</c> to mount it.</summary>
public static class NetApiFlowMcpServiceCollectionExtensions
{
    private const string ServerName = "NetApiFlow";

    /// <summary>Registers the MCP server with HTTP transport and the tools declared in this assembly.</summary>
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMcpServer(options => options.ServerInfo ??= new Implementation
        {
            Name = ServerName,
            Version = typeof(NetApiFlowMcpServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        })
            .WithHttpTransport()
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
