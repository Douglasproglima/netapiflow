using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.Mcp.Tools;

namespace NetApiFlow.Mcp;

/// <summary>Registers the MCP server. See <c>MapNetApiFlowMcp</c> in <c>NetApiFlow.Mcp.Endpoints</c> to mount it.</summary>
public static class NetApiFlowMcpServiceCollectionExtensions
{
    /// <summary>Registers the MCP server with HTTP transport and the tools declared in this assembly.</summary>
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NetApiFlowMcpStartupCheck>());
        services.AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly())
            .WithRequestFilters(filters => filters.AddListToolsFilter(NetApiFlowToolScope.ListTools))
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter(NetApiFlowMcpMessages.Incoming);
                filters.AddOutgoingFilter(NetApiFlowMcpMessages.Outgoing);
            });

        return services;
    }
}
