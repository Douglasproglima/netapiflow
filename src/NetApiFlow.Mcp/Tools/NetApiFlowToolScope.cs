using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetApiFlow.Mcp.Endpoints;

namespace NetApiFlow.Mcp.Tools;

internal static class NetApiFlowToolScope
{
    private static readonly string[] NetApiFlowToolNames = [GetComponentStatsTool.Name, GetTraceTool.Name, ListRecentTracesTool.Name];

    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            return result;
        };

    public static bool IsServedHere(McpServerOptions options, string? toolName) =>
        IsNetApiFlowTool(options, toolName) == NetApiFlowMcpRequest.IsCurrent;

    public static IEnumerable<string> NamesTakenByAnotherTool(McpServerOptions options) =>
        NetApiFlowToolNames;

#pragma warning disable MCP9006
    public static bool CannotTellTheEndpoint(HttpServerTransportOptions options) =>
        options.PerSessionExecutionContext && options.SessionMode != HttpServerSessionMode.Stateless;
#pragma warning restore MCP9006

    public static bool CannotTellTheEndpoint(IServiceProvider services) =>
        CannotTellTheEndpoint(services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value);

    private static bool IsNetApiFlowTool(McpServerOptions options, string? toolName) =>
        toolName is not null
        && options.ToolCollection is { } tools
        && tools.TryGetPrimitive(toolName, out var tool)
        && IsDeclaredByNetApiFlow(tool);

    private static bool IsDeclaredByNetApiFlow(McpServerTool tool) =>
        tool.Metadata.OfType<MethodInfo>().Any(method => method.DeclaringType?.Assembly == typeof(NetApiFlowToolScope).Assembly);
}
