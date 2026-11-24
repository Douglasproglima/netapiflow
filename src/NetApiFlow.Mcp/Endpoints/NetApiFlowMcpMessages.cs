using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetApiFlow.Mcp.Tools;

namespace NetApiFlow.Mcp.Endpoints;

internal static class NetApiFlowMcpMessages
{
    private const string ServerName = "NetApiFlow";
    private const string ServerInfoMetaKey = "io.modelcontextprotocol/serverInfo";

    private static readonly HashSet<string> RequestsServedOnTheNetApiFlowEndpoint =
    [
        RequestMethods.Initialize,
        RequestMethods.ServerDiscover,
        RequestMethods.Ping,
        RequestMethods.ToolsList,
        RequestMethods.ToolsCall,
#pragma warning disable MCP9005
        RequestMethods.LoggingSetLevel,
#pragma warning restore MCP9005
    ];

    private static readonly HashSet<string> NotificationsAcceptedOnTheNetApiFlowEndpoint =
    [
        NotificationMethods.InitializedNotification,
        NotificationMethods.CancelledNotification,
        NotificationMethods.ProgressNotification,
    ];

    private static readonly string ServerVersion =
        typeof(NetApiFlowMcpMessages).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static McpMessageHandler Incoming(McpMessageHandler next) =>
        async (context, cancellationToken) =>
        {
            var onTheNetApiFlowEndpoint = NetApiFlowMcpRequest.IsCurrent;
            switch (context.JsonRpcMessage)
            {
                case JsonRpcRequest request when onTheNetApiFlowEndpoint && !RequestsServedOnTheNetApiFlowEndpoint.Contains(request.Method):
                    throw new McpProtocolException($"Method '{request.Method}' is not available.", McpErrorCode.MethodNotFound);
                case JsonRpcRequest request when request.Method == RequestMethods.ToolsCall
                    && ToolNameOf(request) is var tool
                    && !NetApiFlowToolScope.IsServedHere(context.Server.ServerOptions, tool):
                    throw new McpProtocolException($"Unknown tool: '{tool}'", McpErrorCode.InvalidParams);
                case JsonRpcNotification notification when onTheNetApiFlowEndpoint && !NotificationsAcceptedOnTheNetApiFlowEndpoint.Contains(notification.Method):
                    return;
            }

            await next(context, cancellationToken);
        };

    public static McpMessageHandler Outgoing(McpMessageHandler next) =>
        async (context, cancellationToken) =>
        {
            if (NetApiFlowMcpRequest.IsCurrent && context.JsonRpcMessage is JsonRpcResponse { Result: JsonObject result } response)
            {
                response.Result = WithNetApiFlowAsTheServer(result);
            }

            await next(context, cancellationToken);
        };

    private static string? ToolNameOf(JsonRpcRequest request) =>
        request.Params is JsonObject parameters && parameters["name"] is JsonValue name && name.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject WithNetApiFlowAsTheServer(JsonObject result)
    {
        var named = result.DeepClone().AsObject();
        if (named.ContainsKey("serverInfo"))
        {
            named["serverInfo"] = ServerInfo();
        }

        if (named["_meta"] is JsonObject meta && meta.ContainsKey(ServerInfoMetaKey))
        {
            meta[ServerInfoMetaKey] = ServerInfo();
        }

        return named;
    }

    private static JsonObject ServerInfo() => new() { ["name"] = ServerName, ["version"] = ServerVersion };
}
