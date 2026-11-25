using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using NetApiFlow.Mcp.Tools;

namespace NetApiFlow.Mcp;

internal sealed partial class NetApiFlowMcpStartupCheck(IServiceProvider services, ILogger<NetApiFlowMcpStartupCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (NetApiFlowToolScope.CannotTellTheEndpoint(services))
            {
                Log.ToolsUnavailable(logger);
            }

            foreach (var name in NetApiFlowToolScope.NamesTakenByAnotherTool(services.GetRequiredService<IOptions<McpServerOptions>>().Value))
            {
                Log.ToolNameTaken(logger, name);
            }
        }
        catch (Exception exception)
        {
            Log.CouldNotCheck(logger, exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow.Mcp: the MCP server of this app runs with PerSessionExecutionContext = true and keeps sessions, so a tool call does not carry the HTTP request that sent it, and NetApiFlow cannot tell which endpoint it came from. The NetApiFlow tools are not served on any endpoint, /flow/mcp included. To serve them, set PerSessionExecutionContext = false, or use the stateless mode (SessionMode = HttpServerSessionMode.Stateless) in the options of WithHttpTransport.")]
        public static partial void ToolsUnavailable(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow.Mcp: the MCP server of this app already has a tool named '{ToolName}', registered before the NetApiFlow tool with that name. The NetApiFlow '{ToolName}' tool is not served on any endpoint, and /flow/mcp does not serve the tool of the app. Rename the tool of the app to serve both.")]
        public static partial void ToolNameTaken(ILogger logger, string toolName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow.Mcp: could not check the MCP server options at startup.")]
        public static partial void CouldNotCheck(ILogger logger, Exception exception);
    }
}
