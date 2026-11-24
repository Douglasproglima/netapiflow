using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetApiFlow.Mcp.Endpoints;

namespace NetApiFlow.Mcp.Tools;

internal static class NetApiFlowToolScope
{
    private static readonly HashSet<string> NetApiFlowToolNames =
        [GetComponentStatsTool.Name, GetTraceTool.Name, ListRecentTracesTool.Name];

    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            var onNetApiFlowEndpoint = CameThroughNetApiFlowEndpoint(context.Services);
            result.Tools = [.. result.Tools.Where(tool => NetApiFlowToolNames.Contains(tool.Name) == onNetApiFlowEndpoint)];
            return result;
        };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CallTool(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var tool = context.Params?.Name;
            if (tool is not null && NetApiFlowToolNames.Contains(tool) != CameThroughNetApiFlowEndpoint(context.Services))
            {
                throw new McpProtocolException($"Unknown tool: '{tool}'", McpErrorCode.InvalidParams);
            }

            return await next(context, cancellationToken);
        };

#pragma warning disable MCP9006
    public static bool CannotTellTheEndpoint(HttpServerTransportOptions options) =>
        options.PerSessionExecutionContext && options.SessionMode != HttpServerSessionMode.Stateless;
#pragma warning restore MCP9006

    public static bool CannotTellTheEndpoint(IServiceProvider services) =>
        CannotTellTheEndpoint(services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value);

    private static bool CameThroughNetApiFlowEndpoint(IServiceProvider? services) =>
        services?.GetService<IHttpContextAccessor>()?.HttpContext?.GetEndpoint()?.Metadata.GetMetadata<NetApiFlowMcpEndpoint>() is not null;
}
