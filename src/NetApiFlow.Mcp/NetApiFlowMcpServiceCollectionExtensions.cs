using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using NetApiFlow.Mcp.Tools;

namespace NetApiFlow.Mcp;

/// <summary>Registers the MCP server. See <c>MapNetApiFlowMcp</c> in <c>NetApiFlow.Mcp.Endpoints</c> to mount it.</summary>
public static class NetApiFlowMcpServiceCollectionExtensions
{
    private const string ServerName = "NetApiFlow";

    /// <summary>Registers the MCP server with HTTP transport and the tools declared in this assembly.</summary>
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PerSessionExecutionContextWarning>());
        services.AddMcpServer(options => options.ServerInfo ??= new Implementation
        {
            Name = ServerName,
            Version = typeof(NetApiFlowMcpServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        })
            .WithHttpTransport(options => options.ConfigureSessionOptions = null)
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly())
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(NetApiFlowToolScope.ListTools);
                filters.AddCallToolFilter(NetApiFlowToolScope.CallTool);
            });

        return services;
    }
}
