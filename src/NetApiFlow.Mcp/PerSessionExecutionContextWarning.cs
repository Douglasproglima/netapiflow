using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Mcp.Tools;

namespace NetApiFlow.Mcp;

internal sealed partial class PerSessionExecutionContextWarning(IServiceProvider services, ILogger<PerSessionExecutionContextWarning> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (NetApiFlowToolScope.CannotTellTheEndpoint(services) || services is not null)
            {
                Log.ToolsUnavailable(logger);
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

        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow.Mcp: could not read the options of the MCP HTTP transport at startup.")]
        public static partial void CouldNotCheck(ILogger logger, Exception exception);
    }
}
